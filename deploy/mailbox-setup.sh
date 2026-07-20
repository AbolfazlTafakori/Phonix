#!/usr/bin/env bash
set -euo pipefail

# Gives the shop a mailbox it can actually receive on.
#
# Postfix already accepts mail for the domain (it is in mydestination) and already SENDS as info@, but no
# address has a local destination, so anything arriving is bounced with "user unknown". This adds one real
# mailbox, reachable over IMAPS, without touching the sending path the shop depends on.
#
# What it deliberately does NOT change:
#   • mynetworks, smtpd_relay_restrictions, smtpd_milters — the settings that keep the server from being an
#     open relay and that sign outgoing mail with DKIM. Getting those wrong gets the domain blacklisted, and
#     then even info@ stops reaching customers.
#   • info@ — it stays send-only, as configured.
#
# Every change to main.cf is made through postconf and preceded by a timestamped backup, so a bad run is one
# `cp` away from being undone. The script is idempotent: running it twice is a no-op.

MAIL_USER="${MAIL_USER:-support}"
DOMAIN="${DOMAIN:-}"
MAIL_HOST="${MAIL_HOST:-}"
BACKUP_DIR="/var/backups/phoenix-mail"

C_RESET="\033[0m"; C_BOLD="\033[1m"; C_BLUE="\033[1;34m"; C_GREEN="\033[1;32m"
C_YELLOW="\033[1;33m"; C_RED="\033[1;31m"; C_CYAN="\033[1;36m"

say()  { printf "%b\n" "${C_BLUE}::${C_RESET} $*"; }
ok()   { printf "%b\n" "${C_GREEN}✔${C_RESET} $*"; }
warn() { printf "%b\n" "${C_YELLOW}!${C_RESET} $*"; }
die()  { printf "%b\n" "${C_RED}✖${C_RESET} $*" >&2; exit 1; }
heading() { printf "\n%b\n" "${C_BOLD}${C_CYAN}== $* ==${C_RESET}"; }

require_root() { [[ $EUID -eq 0 ]] || die "Run this as root (use sudo)."; }

# ── Discovery ────────────────────────────────────────────────────────────────────────────────────
# Everything is read from the running Postfix rather than assumed, so the script cannot act on a server
# whose shape it has guessed wrong.
discover() {
    heading "Reading the current setup"

    command -v postconf >/dev/null 2>&1 || die "Postfix is not installed here — this is not the mail server."

    MAIL_HOST="${MAIL_HOST:-$(postconf -h myhostname)}"
    [[ -n "$MAIL_HOST" ]] || die "Could not read myhostname from Postfix."

    # The shop's domain is the mydestination entry that is not the host itself or a localhost alias.
    if [[ -z "$DOMAIN" ]]; then
        DOMAIN="$(postconf -h mydestination | tr ',' '\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' \
            | grep -vE '^(\$myhostname|localhost.*|)$' | grep -v "^${MAIL_HOST}$" | head -1)"
    fi
    [[ -n "$DOMAIN" ]] || die "Could not work out the mail domain; pass it as DOMAIN=example.com."

    say "Mail host : $MAIL_HOST"
    say "Domain    : $DOMAIN"
    say "Mailbox   : ${MAIL_USER}@${DOMAIN}"

    # An MX that points elsewhere means mail will never reach this machine, whatever we configure here.
    local mx
    mx="$(dig +short MX "$DOMAIN" 2>/dev/null | awk '{print $2}' | sed 's/\.$//' | head -1)"
    if [[ -z "$mx" ]]; then
        warn "No MX record found for $DOMAIN — inbound mail will not be routed here until one exists."
    elif [[ "$mx" != "$MAIL_HOST" ]]; then
        warn "MX for $DOMAIN points at '$mx', not '$MAIL_HOST'. Inbound mail goes to that host instead."
    else
        ok "MX points here ($mx)."
    fi

    # The domain must be accepted locally, or Postfix relays/rejects instead of delivering.
    postconf -h mydestination | grep -q "$DOMAIN" \
        || die "'$DOMAIN' is not in mydestination — Postfix would not accept mail for it."
    ok "Postfix accepts mail for $DOMAIN."
}

backup_postfix() {
    mkdir -p "$BACKUP_DIR"
    local stamp; stamp="$(date +%Y%m%d%H%M%S)"
    cp /etc/postfix/main.cf "$BACKUP_DIR/main.cf.$stamp"
    [[ -f /etc/aliases ]] && cp /etc/aliases "$BACKUP_DIR/aliases.$stamp"
    ok "Backed up main.cf and aliases to $BACKUP_DIR (suffix $stamp)."
    say "To undo everything: cp $BACKUP_DIR/main.cf.$stamp /etc/postfix/main.cf && systemctl reload postfix"
}

# ── TLS ──────────────────────────────────────────────────────────────────────────────────────────
# Postfix ships with the self-signed "snakeoil" certificate. That is tolerable for outbound (peers fall back
# to plaintext) but not for IMAP: every mail client would warn on every connection. The shop already has a
# Let's Encrypt certificate; it just needs to cover the mail hostname too.
ensure_cert() {
    heading "Certificate"

    local live="/etc/letsencrypt/live/$DOMAIN"
    [[ -d "$live" ]] || die "No Let's Encrypt certificate at $live — set the site up first."

    if openssl x509 -in "$live/cert.pem" -noout -ext subjectAltName 2>/dev/null | grep -q "DNS:$MAIL_HOST"; then
        ok "Certificate already covers $MAIL_HOST."
        return
    fi

    say "Certificate does not cover $MAIL_HOST — expanding it."
    warn "This needs $MAIL_HOST to resolve to this server and reach it on port 80."
    warn "If $MAIL_HOST sits behind a proxy (Cloudflare orange cloud), set it to DNS-only first."

    # Keep every name the certificate already has, then add the mail host, so expanding never drops a domain
    # the web server is currently serving.
    # Parsed with sed rather than `grep -oP`, which aborts outright on a non-UTF-8 locale. That failure would
    # be silent and expensive: an empty list means asking certbot for a certificate covering ONLY the mail
    # host, dropping the site's own domains from it and taking HTTPS down with them.
    local existing names=()
    existing="$(openssl x509 -in "$live/cert.pem" -noout -ext subjectAltName 2>/dev/null \
        | tr ',' '\n' | sed -n 's/^[[:space:]]*DNS://p' | sed 's/[[:space:]]*$//')"

    while IFS= read -r n; do
        if [[ -n "$n" ]]; then names+=(-d "$n"); fi
    done <<< "$existing"

    # Refuse to guess: stopping with the certificate untouched beats replacing it with a narrower one.
    if [[ ${#names[@]} -eq 0 ]]; then
        die "Could not read the current certificate's domains — refusing to expand it and risk dropping names."
    fi
    names+=(-d "$MAIL_HOST")

    # certbot does NOT infer the authenticator in non-interactive mode, even when the lineage is named — it
    # exits with "Missing command line flags". So the method this certificate already renews with is read
    # from its own renewal config and passed explicitly. That also makes this correct on servers set up
    # differently from the one it was written against, instead of hardcoding a guess.
    local conf="/etc/letsencrypt/renewal/$DOMAIN.conf" auth="" webroot="" auth_flags=()
    [[ -f "$conf" ]] || die "No renewal config at $conf — cannot tell how this certificate is issued."

    auth="$(sed -n 's/^[[:space:]]*authenticator[[:space:]]*=[[:space:]]*//p' "$conf" | head -1 | tr -d '[:space:]')"
    case "$auth" in
        nginx)  auth_flags=(--nginx) ;;
        apache) auth_flags=(--apache) ;;
        webroot)
            # webroot_path is a comma-separated list; the first entry serves the challenge.
            webroot="$(sed -n 's/^[[:space:]]*webroot_path[[:space:]]*=[[:space:]]*//p' "$conf" \
                | head -1 | cut -d, -f1 | tr -d '[:space:]')"
            [[ -n "$webroot" ]] || die "Renewal config says webroot but records no path — expand the certificate by hand."
            auth_flags=(--webroot -w "$webroot")
            ;;
        *)
            die "Unrecognised authenticator '$auth' in $conf.
   Expand the certificate yourself with the method that server uses, then re-run this script."
            ;;
    esac
    say "Using the method this certificate already renews with: $auth"

    certbot certonly --cert-name "$DOMAIN" --expand --non-interactive --agree-tos --keep-until-expiring \
        "${auth_flags[@]}" "${names[@]}" \
        || die "certbot failed — the certificate is unchanged and nothing else has been touched yet.
   Check that $MAIL_HOST resolves here and is reachable on port 80, then re-run.
   Full detail: /var/log/letsencrypt/letsencrypt.log"
    ok "Certificate now covers $MAIL_HOST."
}

point_postfix_at_cert() {
    local live="/etc/letsencrypt/live/$DOMAIN"
    local current; current="$(postconf -h smtpd_tls_cert_file)"

    if [[ "$current" == "$live/fullchain.pem" ]]; then
        ok "Postfix already uses the real certificate."
        return
    fi

    postconf -e "smtpd_tls_cert_file=$live/fullchain.pem"
    postconf -e "smtpd_tls_key_file=$live/privkey.pem"
    ok "Postfix now presents the real certificate instead of snakeoil."
}

# ── Mailbox ──────────────────────────────────────────────────────────────────────────────────────
# Maildir (one file per message) rather than mbox (one file for everything): Dovecot handles it better and a
# corrupt message can never take the whole mailbox with it.
create_mailbox() {
    heading "Mailbox"

    if id -u "$MAIL_USER" >/dev/null 2>&1; then
        ok "User '$MAIL_USER' already exists."
    else
        # No shell: this account exists to hold mail, not to log into the server with.
        adduser --disabled-password --gecos "Phoenix support mailbox" --shell /usr/sbin/nologin "$MAIL_USER"
        ok "Created user '$MAIL_USER' (no shell login)."
        warn "Set its mailbox password now — this is what the mail client will use:"
        passwd "$MAIL_USER"
    fi

    local maildir="/home/$MAIL_USER/Maildir"
    if [[ ! -d "$maildir" ]]; then
        mkdir -p "$maildir"/{cur,new,tmp}
        chown -R "$MAIL_USER:$MAIL_USER" "$maildir"
        chmod -R 700 "$maildir"
        ok "Created $maildir."
    fi

    # Deliver to Maildir rather than /var/mail/<user>. Trailing slash is what selects Maildir format.
    if [[ "$(postconf -h home_mailbox)" != "Maildir/" ]]; then
        postconf -e "home_mailbox=Maildir/"
        ok "Postfix now delivers to Maildir."
    fi

    if grep -qE "^${MAIL_USER}:" /etc/aliases 2>/dev/null; then
        ok "Alias for ${MAIL_USER}@ already present."
    else
        echo "${MAIL_USER}: ${MAIL_USER}" >> /etc/aliases
        newaliases
        ok "Mail for ${MAIL_USER}@${DOMAIN} now lands in that mailbox."
    fi

    # A fresh Maildir has only INBOX; the standard folders are defined in Dovecot's namespace but not created
    # until something uses them. The panel needs them up front: replies are filed in Sent, and its
    # archive/spam/delete actions move messages into Archive/Junk/Trash. Created here (idempotent) so those
    # work on first use rather than silently no-op'ing against a folder that does not exist yet. Deferred to
    # apply() via a flag, because doveadm needs Dovecot already running with the config this script writes.
    NEEDS_STD_FOLDERS=1
}

# ── IMAP ─────────────────────────────────────────────────────────────────────────────────────────
install_dovecot() {
    heading "IMAP"

    if ! dpkg -s dovecot-imapd >/dev/null 2>&1; then
        say "Installing Dovecot..."
        DEBIAN_FRONTEND=noninteractive apt-get update -qq
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dovecot-imapd
        ok "Dovecot installed."
    else
        ok "Dovecot already installed."
    fi

    # Dovecot 2.4 renamed most of what this file sets — ssl_cert became ssl_server_cert_file, and
    # mail_location split into mail_driver + mail_path. Writing 2.3 syntax to a 2.4 server is a fatal config
    # error, so the version decides the grammar rather than an assumption about which one is installed.
    local ver major minor
    ver="$(dovecot --version 2>/dev/null | awk '{print $1}')"
    major="${ver%%.*}"; minor="$(printf '%s' "$ver" | cut -d. -f2)"
    [[ -n "$major" && -n "$minor" ]] || die "Could not read the Dovecot version — refusing to guess its config syntax."
    say "Dovecot $ver detected."

    # One drop-in file rather than edits scattered through the stock config: everything this script owns
    # lives here, so it is obvious what was added and trivial to remove.
    {
        echo "# Managed by deploy/mailbox-setup.sh — remove this file to undo the IMAP setup."
        echo

        if (( major > 2 || (major == 2 && minor >= 4) )); then
            echo "mail_driver = maildir"
            echo "mail_path = %{home}/Maildir"
            # 2.4 introduced mail_inbox_path, defaulting to /var/mail/%{user} — the old mbox spool. Left at
            # that default, Dovecot autocreates INBOX at /var/mail/<user>, which the mailbox user cannot write
            # (permission denied), AND reads INBOX from there while Postfix delivers to ~/Maildir — so mail
            # would land in one place and be read from another. Blanking it makes INBOX use mail_path, so it
            # is the Maildir Postfix delivers to.
            echo "mail_inbox_path ="
            echo
            echo "ssl_server_cert_file = /etc/letsencrypt/live/$DOMAIN/fullchain.pem"
            echo "ssl_server_key_file = /etc/letsencrypt/live/$DOMAIN/privkey.pem"
        else
            echo "mail_location = maildir:~/Maildir"
            echo
            echo "ssl = required"
            echo "ssl_cert = </etc/letsencrypt/live/$DOMAIN/fullchain.pem"
            echo "ssl_key = </etc/letsencrypt/live/$DOMAIN/privkey.pem"
        fi

        echo "ssl_min_protocol = TLSv1.2"
        echo
        echo "# Credentials may never cross the network in the clear; Dovecot refuses plaintext auth"
        echo "# unless the connection is already TLS, which covers both 993 and STARTTLS on 143."
        echo "auth_allow_cleartext = no"
        echo "auth_mechanisms = plain login"
    } > /etc/dovecot/conf.d/99-phoenix.conf

    # Validate before anything restarts. Dovecot refuses to start on a bad setting, and finding that out
    # here — with the file still removable — beats finding out from a dead service.
    if ! doveconf -n >/dev/null 2>/tmp/dovecot-check.err; then
        local reason; reason="$(cat /tmp/dovecot-check.err)"
        rm -f /etc/dovecot/conf.d/99-phoenix.conf
        die "Dovecot rejected the configuration, so it was removed and nothing was restarted:
   $reason"
    fi
    ok "Wrote /etc/dovecot/conf.d/99-phoenix.conf"
}

# Certbot renews silently every ~60 days. Both daemons read the certificate once at start, so without this
# they would keep serving the expired one long after the file on disk was replaced — and the failure would
# only show up as mail clients refusing to connect.
install_renewal_hook() {
    local hook=/etc/letsencrypt/renewal-hooks/deploy/reload-mail.sh
    mkdir -p "$(dirname "$hook")"
    cat > "$hook" <<'HOOK'
#!/usr/bin/env bash
# Managed by deploy/mailbox-setup.sh — reloads the mail daemons after a certificate renewal.
systemctl reload postfix 2>/dev/null || true
systemctl reload dovecot 2>/dev/null || true
HOOK
    chmod +x "$hook"
    ok "Renewal hook installed — the mail daemons pick up each renewed certificate."
}

open_firewall() {
    command -v ufw >/dev/null 2>&1 || return 0
    ufw status 2>/dev/null | grep -q "Status: active" || return 0

    # 25 is for RECEIVING: other mail servers connect here to deliver. Without it the MX points at a port the
    # firewall drops, so mail is silently deferred and never arrives — with nothing in the local log to show
    # for it. 587 is deliberately NOT opened: the shop's backend submits from localhost, which ufw never
    # blocks, so exposing the authenticated submission port to the internet would only invite brute force.
    for port in 25 993; do
        if ufw status | grep -qE "^${port}(/tcp)?\b"; then
            ok "Firewall already allows ${port}."
        else
            ufw allow "${port}/tcp" >/dev/null
            ok "Opened port ${port}/tcp."
        fi
    done
}

apply() {
    heading "Applying"
    postfix check || die "Postfix rejected the configuration — nothing reloaded. Restore from $BACKUP_DIR."
    systemctl reload postfix
    ok "Postfix reloaded."
    systemctl enable --now dovecot >/dev/null 2>&1 || die "Dovecot failed to start — check: journalctl -u dovecot -n 50"
    systemctl restart dovecot
    ok "Dovecot running."

    # Now that Dovecot is up with the final config, create the standard folders the panel relies on. doveadm
    # needs a running server, so this cannot happen back in ensure_mailbox. Both commands are idempotent —
    # an existing folder is left untouched — so re-runs are safe.
    if [[ "${NEEDS_STD_FOLDERS:-0}" == "1" ]]; then
        doveadm mailbox create    -u "$MAIL_USER" Sent Drafts Junk Trash Archive >/dev/null 2>&1 || true
        doveadm mailbox subscribe -u "$MAIL_USER" Sent Drafts Junk Trash Archive >/dev/null 2>&1 || true
        ok "Ensured standard folders (Sent, Drafts, Junk, Trash, Archive)."
    fi
}

verify() {
    heading "Verifying"
    systemctl is-active --quiet postfix && ok "postfix active" || warn "postfix is NOT active"
    systemctl is-active --quiet dovecot && ok "dovecot active" || warn "dovecot is NOT active"
    ss -tlnp 2>/dev/null | grep -q ':993' && ok "listening on 993 (IMAPS)" || warn "nothing is listening on 993"

    printf "\n%b\n" "${C_BOLD}Mail client settings${C_RESET}"
    echo "  Address  : ${MAIL_USER}@${DOMAIN}"
    echo "  IMAP     : ${MAIL_HOST}   port 993   SSL/TLS"
    echo "  SMTP     : ${MAIL_HOST}   port 587   STARTTLS"
    echo "  Username : ${MAIL_USER}"
    echo "  Password : the one set with passwd above"
    printf "\n%b\n" "${C_BOLD}Test it${C_RESET}"
    echo "  Send a message to ${MAIL_USER}@${DOMAIN} from an outside address, then:"
    echo "    ls -l /home/${MAIL_USER}/Maildir/new/"
    echo "  A file there means delivery works. If not: journalctl -u postfix -n 50"
}

main() {
    require_root
    heading "Phoenix mailbox setup"
    discover
    backup_postfix
    ensure_cert
    point_postfix_at_cert
    create_mailbox
    install_dovecot
    install_renewal_hook
    open_firewall
    apply
    verify
    printf "\n"
    ok "Done. Sending was not modified — info@ still goes out exactly as before."
}

main "$@"
