#!/usr/bin/env bash
set -euo pipefail

REPO_URL="https://github.com/AbolfazlTafakori/Phonix.git"
APP_USER="phoenix"
BASE_DIR="/opt/phoenix"
REPO_DIR="$BASE_DIR/repo"
RELEASES_DIR="$BASE_DIR/releases"
CURRENT_LINK="$BASE_DIR/current"
DATA_DIR="/var/lib/phoenix"
LOG_DIR="/var/log/phoenix"
CONF_DIR="/etc/phoenix"
ENV_FILE="$CONF_DIR/phoenix.env"
SECRET_FILE="$CONF_DIR/secret.env"
OWNER_FILE="$CONF_DIR/owner.env"
NGINX_SITE="/etc/nginx/sites-available/phoenix.conf"
NGINX_LINK="/etc/nginx/sites-enabled/phoenix.conf"
SRC_TARBALL_URL="https://codeload.github.com/AbolfazlTafakori/Phonix/tar.gz/refs/heads/main"
PUI_SRC="$REPO_DIR/deploy/p-ui"
PUI_PATH="/usr/local/bin/p-ui"
API_PORT=5228
WEB_PORT=3000
DOTNET_PROJECT="backend/src/Phonix.Api"

C_RESET="\033[0m"; C_BOLD="\033[1m"; C_BLUE="\033[1;34m"; C_GREEN="\033[1;32m"
C_YELLOW="\033[1;33m"; C_RED="\033[1;31m"; C_CYAN="\033[1;36m"

say()  { printf "%b\n" "${C_BLUE}::${C_RESET} $*"; }
ok()   { printf "%b\n" "${C_GREEN}✔${C_RESET} $*"; }
warn() { printf "%b\n" "${C_YELLOW}!${C_RESET} $*"; }
die()  { printf "%b\n" "${C_RED}✖${C_RESET} $*" >&2; exit 1; }
heading() { printf "\n%b\n" "${C_BOLD}${C_CYAN}== $* ==${C_RESET}"; }

require_root() {
    [[ $EUID -eq 0 ]] || die "This installer must run as root (use sudo)."
}

# Where this script is being run from. When the installer sits inside an extracted copy of the source
# (deploy/install.sh next to backend/ and frontend/), that copy is what gets installed and no download
# is attempted. Piping the script in (bash <(curl ...)) leaves BASH_SOURCE pointing at a file
# descriptor rather than a real tree, which simply falls through to the network path below.
detect_local_source() {
    local self dir
    self="${BASH_SOURCE[0]}"
    [[ -f "$self" ]] || return 0
    dir="$(cd "$(dirname "$self")/.." 2>/dev/null && pwd)" || return 0
    [[ -d "$dir/backend" && -d "$dir/frontend" ]] || return 0
    printf '%s' "$dir"
}

# Builds need packages from nuget.org. Where that host is unreachable, restore hangs until it times out
# and then fails, even though the packages may already be in the local cache (copied from a machine that
# does have access). Pointing NuGet at no remote source makes it resolve straight from that cache.
ensure_offline_nuget() {
    if curl -fsS -o /dev/null -m 8 https://api.nuget.org/v3/index.json 2>/dev/null; then
        rm -f "$REPO_DIR/nuget.config"
        return 0
    fi
    if [[ ! -d "$HOME/.nuget/packages" ]]; then
        warn "nuget.org is unreachable and there is no local package cache — the backend build will fail."
        return 0
    fi
    warn "nuget.org is unreachable — building the backend from the local package cache."
    cat > "$REPO_DIR/nuget.config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
</configuration>
XML
}

# Plain HTTPS download of the source, used when the git protocol is blocked but ordinary web traffic
# is not. Retried, and only swapped in once the extracted tree is confirmed to look like the repo.
fetch_source_archive() {
    local tmp dir
    tmp="$(mktemp -d)"
    if ! curl -fL --retry 5 --retry-delay 3 --retry-all-errors --connect-timeout 20 \
        -o "$tmp/src.tar.gz" "$SRC_TARBALL_URL"; then
        rm -rf "$tmp"; return 1
    fi
    if ! tar xzf "$tmp/src.tar.gz" -C "$tmp"; then
        rm -rf "$tmp"; return 1
    fi
    dir="$(find "$tmp" -mindepth 1 -maxdepth 1 -type d | head -n1)"
    if [[ ! -d "$dir/backend" || ! -d "$dir/frontend" ]]; then
        rm -rf "$tmp"; return 1
    fi
    mkdir -p "$REPO_DIR"
    rsync -a --delete --exclude .git "$dir/" "$REPO_DIR/"
    rm -rf "$tmp"
    return 0
}

require_ubuntu() {
    command -v apt-get >/dev/null 2>&1 || die "This installer supports Ubuntu/Debian only."
}

validate_domain() {
    [[ "$1" =~ ^([a-zA-Z0-9](-?[a-zA-Z0-9])*\.)+[a-zA-Z]{2,}$ ]]
}

validate_username() {
    [[ "$1" =~ ^[a-zA-Z0-9_]{3,32}$ ]]
}

validate_password() {
    local p="$1"
    [[ ${#p} -ge 12 ]] || return 1
    [[ "$p" =~ [A-Z] ]] || return 1
    [[ "$p" =~ [a-z] ]] || return 1
    [[ "$p" =~ [0-9] ]] || return 1
    [[ "$p" =~ [^A-Za-z0-9] ]] || return 1
    return 0
}

prompt_domain() {
    while true; do
        read -rp "$(printf "%b" "${C_BOLD}Site domain (e.g. shop.example.com): ${C_RESET}")" DOMAIN
        DOMAIN="${DOMAIN,,}"
        if validate_domain "$DOMAIN"; then break; fi
        warn "Invalid domain. Please try again."
    done

    read -rp "$(printf "%b" "${C_BOLD}Email for the Let's Encrypt certificate: ${C_RESET}")" LE_EMAIL
    [[ -n "$LE_EMAIL" ]] || die "An email is required for Certbot."
}

prompt_owner() {
    while true; do
        read -rp "$(printf "%b" "${C_BOLD}Owner username: ${C_RESET}")" OWNER_USER
        if validate_username "$OWNER_USER"; then break; fi
        warn "Username must be 3-32 chars: letters, numbers, underscore only."
    done

    while true; do
        read -rsp "$(printf "%b" "${C_BOLD}Owner password (min 12 chars, with upper, lower, number and symbol): ${C_RESET}")" OWNER_PASS; echo
        if ! validate_password "$OWNER_PASS"; then
            warn "Password does not meet the complexity requirements."
            continue
        fi
        read -rsp "$(printf "%b" "${C_BOLD}Confirm password: ${C_RESET}")" OWNER_PASS2; echo
        if [[ "$OWNER_PASS" != "$OWNER_PASS2" ]]; then
            warn "Passwords do not match."
            continue
        fi
        break
    done
}

install_dependencies() {
    heading "Installing dependencies"
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -y
    apt-get install -y curl wget gnupg ca-certificates lsb-release apt-transport-https git rsync openssl ufw

    if ! command -v dotnet >/dev/null 2>&1; then
        local rel; rel="$(lsb_release -rs)"
        local installed=0

        # Preferred path: Microsoft's apt feed. Only some Ubuntu releases carry dotnet-sdk-8.0,
        # so treat any failure here as non-fatal and fall back to the official install script.
        if wget -q "https://packages.microsoft.com/config/ubuntu/${rel}/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb; then
            dpkg -i /tmp/packages-microsoft-prod.deb || true
            rm -f /tmp/packages-microsoft-prod.deb
            apt-get update -y || true
            if apt-get install -y dotnet-sdk-8.0; then
                installed=1
            fi
        fi

        # Distro-agnostic fallback: the official dotnet-install script. Pins .NET 8 SDK into
        # /usr/share/dotnet and exposes it on PATH via /usr/bin/dotnet (matches the systemd ExecStart).
        if [[ $installed -ne 1 ]]; then
            warn "dotnet-sdk-8.0 not available from apt on Ubuntu ${rel}; using the official install script."
            curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
            bash /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
            rm -f /tmp/dotnet-install.sh
            ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
        fi
    fi
    ok ".NET $(dotnet --version)"

    if ! command -v node >/dev/null 2>&1; then
        curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
        apt-get install -y nodejs
    fi
    ok "Node $(node --version)"

    apt-get install -y nginx certbot python3-certbot-nginx
    ok "Nginx + Certbot"
}

create_user_and_dirs() {
    heading "Creating service user and directories"
    if ! id "$APP_USER" >/dev/null 2>&1; then
        useradd --system --create-home --home-dir "/home/$APP_USER" --shell /usr/sbin/nologin "$APP_USER"
    fi
    mkdir -p "$BASE_DIR" "$RELEASES_DIR" "$DATA_DIR" "$LOG_DIR" "$CONF_DIR"
    chown -R "$APP_USER:$APP_USER" "$BASE_DIR" "$DATA_DIR" "$LOG_DIR"
    chmod 750 "$CONF_DIR"
    ok "Directories ready"
}

fetch_repo() {
    heading "Fetching source code"
    # create_user_and_dirs chowns $BASE_DIR (which contains $REPO_DIR) to $APP_USER, so git now runs
    # as root over a repo owned by another user. Mark it trusted to avoid "dubious ownership" aborts.
    git config --global --add safe.directory "$REPO_DIR"
    local local_src; local_src="$(detect_local_source)"

    if [[ -n "$local_src" ]]; then
        # Running from an extracted copy — install exactly that, no network involved.
        if [[ "$local_src" != "$REPO_DIR" ]]; then
            say "Installing from the extracted source at $local_src"
            mkdir -p "$REPO_DIR"
            rsync -a --delete --exclude .git "$local_src/" "$REPO_DIR/"
        else
            say "Installing from the source already at $REPO_DIR"
        fi
    elif [[ -d "$REPO_DIR/.git" ]] \
        && git -C "$REPO_DIR" fetch --all --prune \
        && git -C "$REPO_DIR" reset --hard origin/main; then
        ok "Updated to the latest origin/main."
    elif git clone "$REPO_URL" "$REPO_DIR" 2>/dev/null; then
        ok "Cloned from $REPO_URL"
    elif fetch_source_archive; then
        # git is blocked on some networks while ordinary HTTPS still works.
        ok "Downloaded the source archive over HTTPS."
    elif [[ -d "$REPO_DIR/backend" && -d "$REPO_DIR/frontend" ]]; then
        warn "Could not reach GitHub — building from the source already in $REPO_DIR."
    else
        die "Could not reach GitHub and no source is available locally. Extract the repository archive and run deploy/install.sh from inside it."
    fi
    chown -R "$APP_USER:$APP_USER" "$REPO_DIR"
    ok "Source ready at $REPO_DIR"
}

generate_backup_key() {
    heading "Generating backup encryption key"
    if [[ -f "$SECRET_FILE" ]] && grep -q '^PHONIX_BACKUP_KEY=' "$SECRET_FILE"; then
        BACKUP_KEY="$(grep '^PHONIX_BACKUP_KEY=' "$SECRET_FILE" | cut -d= -f2-)"
        warn "An existing backup key was found and preserved."
    else
        BACKUP_KEY="$(openssl rand -base64 32)"
        umask 077
        printf 'PHONIX_BACKUP_KEY=%s\n' "$BACKUP_KEY" > "$SECRET_FILE"
        chmod 600 "$SECRET_FILE"
        chown root:root "$SECRET_FILE"
        ok "Generated a 256-bit key"
    fi
}

write_env_files() {
    heading "Writing environment configuration"
    umask 077
    cat > "$ENV_FILE" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:$API_PORT
PHONIX_DOMAIN=$DOMAIN
PHONIX_FRONTEND_URL=https://$DOMAIN
PHONIX_BEHIND_PROXY=true
PHONIX_TRUSTED_PROXIES=127.0.0.1
PHONIX_FORCE_HTTPS=true
PHONIX_DATA_FILE=$DATA_DIR/store.json
PHONIX_AUDIT_FILE=$DATA_DIR/audit_store.json
PHONIX_KEYS_DIR=$DATA_DIR/keys
PHONIX_UPLOADS_DIR=$DATA_DIR/uploads
PHONIX_LOG_DIR=$LOG_DIR
NODE_ENV=production
PORT=$WEB_PORT
NEXT_PUBLIC_API_URL=
PHONIX_INTERNAL_API_URL=http://127.0.0.1:$API_PORT
EOF
    chmod 640 "$ENV_FILE"; chown root:"$APP_USER" "$ENV_FILE"

    printf 'PHONIX_OWNER_USERNAME=%s\nPHONIX_OWNER_PASSWORD=%s\n' "$OWNER_USER" "$OWNER_PASS" > "$OWNER_FILE"
    chmod 600 "$OWNER_FILE"; chown root:root "$OWNER_FILE"
    ok "Configuration written"
}

build_release() {
    heading "Building release"
    local rel="$RELEASES_DIR/$(date +%Y%m%d%H%M%S)"
    mkdir -p "$rel/api" "$rel/web"

    # A release built elsewhere can be handed to the installer with PREBUILT_RELEASE=/path/to/release.
    # Building needs NuGet and npm, which some networks block outright; on those hosts the artifacts are
    # produced on a machine that does have access and copied over, and this host only ever runs them.
    if [[ -n "${PREBUILT_RELEASE:-}" ]]; then
        [[ -d "$PREBUILT_RELEASE/api" && -d "$PREBUILT_RELEASE/web" ]] \
            || die "PREBUILT_RELEASE=$PREBUILT_RELEASE must contain both api/ and web/."
        [[ -f "$PREBUILT_RELEASE/api/Phonix.Api.dll" ]] \
            || die "No Phonix.Api.dll under $PREBUILT_RELEASE/api — that is not a published backend."
        [[ -d "$PREBUILT_RELEASE/web/.next" ]] \
            || die "No .next build under $PREBUILT_RELEASE/web — the frontend was not built."
        say "Using the prebuilt release at $PREBUILT_RELEASE (skipping compilation)"
        rsync -a "$PREBUILT_RELEASE/api/" "$rel/api/"
        rsync -a "$PREBUILT_RELEASE/web/" "$rel/web/"
    else
        ensure_offline_nuget
        dotnet publish "$REPO_DIR/$DOTNET_PROJECT" -c Release -o "$rel/api" --nologo

        rsync -a --delete --exclude node_modules --exclude .next "$REPO_DIR/frontend/" "$rel/web/"
        ( cd "$rel/web" && npm ci && NEXT_PUBLIC_API_URL="" NODE_ENV=production npm run build )
    fi

    chown -R "$APP_USER:$APP_USER" "$rel"
    ln -sfn "$rel" "$CURRENT_LINK"
    chown -h "$APP_USER:$APP_USER" "$CURRENT_LINK"
    ok "Release built: $rel"
}

write_systemd_units() {
    heading "Registering systemd services"
    cat > /etc/systemd/system/phoenix-api.service <<EOF
[Unit]
Description=Phoenix API
After=network.target

[Service]
Type=simple
User=$APP_USER
Group=$APP_USER
WorkingDirectory=$CURRENT_LINK/api
ExecStart=/usr/bin/dotnet $CURRENT_LINK/api/Phonix.Api.dll
EnvironmentFile=$ENV_FILE
EnvironmentFile=$SECRET_FILE
EnvironmentFile=$OWNER_FILE
Restart=always
RestartSec=3
KillSignal=SIGINT
TimeoutStopSec=20
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

    cat > /etc/systemd/system/phoenix-web.service <<EOF
[Unit]
Description=Phoenix Web
After=network.target phoenix-api.service

[Service]
Type=simple
User=$APP_USER
Group=$APP_USER
WorkingDirectory=$CURRENT_LINK/web
ExecStart=/usr/bin/npm run start
EnvironmentFile=$ENV_FILE
Restart=always
RestartSec=3
TimeoutStopSec=20
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable phoenix-api phoenix-web >/dev/null 2>&1
    ok "Services registered"
}

configure_nginx() {
    heading "Configuring Nginx"
    cat > "$NGINX_SITE" <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN;

    client_max_body_size 50m;

    location /api/ {
        proxy_pass http://127.0.0.1:$API_PORT;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location / {
        proxy_pass http://127.0.0.1:$WEB_PORT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF
    ln -sfn "$NGINX_SITE" "$NGINX_LINK"
    rm -f /etc/nginx/sites-enabled/default
    nginx -t
    systemctl reload nginx
    ok "Nginx configured"
}

provision_ssl() {
    heading "Issuing SSL certificate"
    certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos -m "$LE_EMAIL" --redirect
    systemctl reload nginx
    ok "HTTPS enabled"
}

configure_firewall() {
    heading "Configuring firewall"
    ufw allow OpenSSH >/dev/null 2>&1 || true
    ufw allow 'Nginx Full' >/dev/null 2>&1 || true
    yes | ufw enable >/dev/null 2>&1 || true
    ok "Firewall enabled"
}

install_pui() {
    heading "Installing the p-ui management tool"
    if [[ -f "$PUI_SRC" ]]; then
        install -m 0755 "$PUI_SRC" "$PUI_PATH"
    else
        die "p-ui script not found at $PUI_SRC"
    fi
    ok "The p-ui command is now available"
}

start_services() {
    heading "Starting services"
    systemctl restart phoenix-api
    systemctl restart phoenix-web
    ok "Services running"
}

print_summary() {
    printf "\n%b\n" "${C_GREEN}${C_BOLD}Installation completed successfully.${C_RESET}"
    printf "%b\n" "Site URL:   ${C_CYAN}https://$DOMAIN${C_RESET}"
    printf "%b\n" "Owner:      ${C_CYAN}$OWNER_USER${C_RESET}"
    printf "%b\n" "Management: run ${C_CYAN}p-ui${C_RESET}"

    printf "\n%b\n" "${C_RED}${C_BOLD}┌────────────────────────────────────────────────────────────────┐${C_RESET}"
    printf "%b\n"   "${C_RED}${C_BOLD}│  PHONIX_BACKUP_KEY — shown only once, right now                 │${C_RESET}"
    printf "%b\n"   "${C_RED}${C_BOLD}└────────────────────────────────────────────────────────────────┘${C_RESET}"
    printf "\n    %b\n\n" "${C_BOLD}${BACKUP_KEY}${C_RESET}"
    printf "%b\n" "${C_YELLOW}Save this key offline securely right now. Encrypted backups cannot be restored without it, and it will never be displayed in any menu or tool again.${C_RESET}\n"
}

main() {
    require_root
    require_ubuntu
    heading "Phoenix Store Installer"
    prompt_domain
    prompt_owner
    install_dependencies
    create_user_and_dirs
    fetch_repo
    generate_backup_key
    write_env_files
    build_release
    write_systemd_units
    configure_nginx
    provision_ssl
    configure_firewall
    install_pui
    start_services
    print_summary
}

main "$@"
