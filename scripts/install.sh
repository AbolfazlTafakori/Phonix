#!/usr/bin/env bash
# Phonix one-line installer for a fresh Ubuntu/Debian server (e.g. Hetzner Cloud).
#
# Run as root (or with sudo). Two ways to use it:
#
#   1) From inside an already-cloned repo:
#        sudo bash scripts/install.sh
#
#   2) Bootstrap on a bare server (clones the repo for you):
#        curl -fsSL https://raw.githubusercontent.com/<user>/<repo>/main/scripts/install.sh \
#          | sudo REPO_URL=https://github.com/<user>/<repo>.git bash
#
# Optional environment overrides:
#   REPO_URL    git URL to clone when not run inside the repo
#   APP_DIR     install location (default: /opt/phonix)
#   DOMAIN      public host for both apps; if set, URLs become https://DOMAIN + https://DOMAIN/api
#   SERVER_IP   public IP used to build default http URLs (auto-detected when unset)
#   BRANCH      git branch to deploy (default: main)
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/phonix}"
BRANCH="${BRANCH:-main}"

log()  { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!  \033[0m %s\n' "$*"; }
die()  { printf '\033[1;31mx  \033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Please run as root (or with sudo)."

# 1) Docker Engine + compose plugin -------------------------------------------------
if ! command -v docker >/dev/null 2>&1; then
  log "Installing Docker Engine..."
  curl -fsSL https://get.docker.com | sh
else
  log "Docker already installed: $(docker --version)"
fi

if ! docker compose version >/dev/null 2>&1; then
  die "The Docker Compose plugin is missing. Install 'docker-compose-plugin' and re-run."
fi

systemctl enable --now docker >/dev/null 2>&1 || true

# 2) Get the code -------------------------------------------------------------------
if [ -f "docker-compose.yml" ] && [ -d "backend" ] && [ -d "frontend" ]; then
  APP_DIR="$(pwd)"
  log "Using repo in current directory: $APP_DIR"
  git pull --ff-only 2>/dev/null || true
elif [ -f "$APP_DIR/docker-compose.yml" ]; then
  log "Updating existing install at $APP_DIR"
  cd "$APP_DIR"
  git pull --ff-only 2>/dev/null || true
else
  [ -n "${REPO_URL:-}" ] || die "Not inside the repo and REPO_URL is not set. See the header of this script."
  command -v git >/dev/null 2>&1 || { log "Installing git..."; apt-get update -y && apt-get install -y git; }
  log "Cloning $REPO_URL → $APP_DIR (branch $BRANCH)"
  git clone --branch "$BRANCH" --depth 1 "$REPO_URL" "$APP_DIR"
  cd "$APP_DIR"
fi

# 3) Build .env ---------------------------------------------------------------------
if [ ! -f .env ]; then
  log "Creating .env from .env.example"
  cp .env.example .env

  if [ -n "${DOMAIN:-}" ]; then
    FRONTEND_URL="https://${DOMAIN}"
    API_URL="https://${DOMAIN}/api"
    log "Configuring for domain ${DOMAIN} (enable a TLS reverse proxy — see DEPLOY.md)"
    sed -i "s#^PHONIX_BEHIND_PROXY=.*#PHONIX_BEHIND_PROXY=true#" .env
    sed -i "s#^PHONIX_FORCE_HTTPS=.*#PHONIX_FORCE_HTTPS=true#" .env
  else
    SERVER_IP="${SERVER_IP:-$(curl -fsS https://api.ipify.org 2>/dev/null || hostname -I | awk '{print $1}')}"
    FRONTEND_URL="http://${SERVER_IP}:3000"
    API_URL="http://${SERVER_IP}:5228"
    log "No DOMAIN set — using server IP ${SERVER_IP} over HTTP"
  fi

  sed -i "s#^NEXT_PUBLIC_API_URL=.*#NEXT_PUBLIC_API_URL=${API_URL}#" .env
  sed -i "s#^PHONIX_FRONTEND_URL=.*#PHONIX_FRONTEND_URL=${FRONTEND_URL}#" .env
else
  warn ".env already exists — leaving it untouched."
fi

# 4) Build + run --------------------------------------------------------------------
log "Building and starting containers (this can take a few minutes on first run)..."
docker compose up -d --build
docker image prune -f >/dev/null 2>&1 || true

# 5) Auto-start on reboot -----------------------------------------------------------
# Docker is enabled on boot and the containers use `restart: unless-stopped`, so they
# already survive reboots. The systemd unit adds a clean control surface and re-runs
# `compose up -d` on boot as a belt-and-suspenders so a reboot never needs a human.
if command -v systemctl >/dev/null 2>&1; then
  log "Enabling auto-start on reboot (systemd unit phonix.service)"
  sed "s#^WorkingDirectory=.*#WorkingDirectory=${APP_DIR}#" scripts/phonix.service \
    > /etc/systemd/system/phonix.service
  systemctl daemon-reload
  systemctl enable phonix.service >/dev/null 2>&1 || true
else
  warn "systemd not found — relying on the compose restart policy for reboot recovery."
fi

log "Done. Containers:"
docker compose ps

# shellcheck disable=SC1091
set -a; . ./.env; set +a
echo
log "Storefront: ${PHONIX_FRONTEND_URL:-http://<server>:3000}"
log "API:        ${NEXT_PUBLIC_API_URL:-http://<server>:5228}"
warn "First admin login uses the seeded credentials — change the password right after logging in."
