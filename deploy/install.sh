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
    if [[ -d "$REPO_DIR/.git" ]]; then
        git -C "$REPO_DIR" fetch --all --prune
        git -C "$REPO_DIR" reset --hard origin/main
    else
        git clone "$REPO_URL" "$REPO_DIR"
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

    dotnet publish "$REPO_DIR/$DOTNET_PROJECT" -c Release -o "$rel/api" --nologo

    rsync -a --delete --exclude node_modules --exclude .next "$REPO_DIR/frontend/" "$rel/web/"
    ( cd "$rel/web" && npm ci && NEXT_PUBLIC_API_URL="" NODE_ENV=production npm run build )

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
