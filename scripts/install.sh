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
#   REPO_URL       git URL to clone when not run inside the repo
#   APP_DIR        install location (default: /opt/phonix)
#   DOMAIN         public host for both apps; if set, URLs become https://DOMAIN + https://DOMAIN/api
#   SERVER_IP      public IP used to build default http URLs (auto-detected when unset)
#   BRANCH         git branch to deploy (default: main)
#   OWNER_USERNAME first admin (owner) account username — skips the interactive prompt when set
#   OWNER_PASSWORD first admin (owner) account password — skips the interactive prompt when set
#   CLUSTER_MODE   standalone (default) | primary | standby — optional High Availability, business
#                  continuity only; skips the interactive prompt when set
#   NODE_ID        a short label for this server (e.g. germany) — Primary/Standby only
#   CLUSTER_PEER   the OTHER cluster node's reachable base URL — Primary/Standby only
#   CLUSTER_SECRET shared secret authenticating node-to-node calls — Primary/Standby only; the Primary
#                  auto-generates one if not set, the Standby must be given that SAME value
#
# The owner account is set on THIS terminal at install time (typed interactively, never generated
# or printed back) — it's the one account that exists before anything else. The owner then creates
# any other staff/admin accounts from the admin panel itself; there is no other way in.
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

  # First admin (owner) account — typed on this terminal, never auto-generated or echoed back. Reads
  # from /dev/tty so this still works when the script is piped via `curl | sudo bash` (stdin in that
  # case is the script itself, not the keyboard). This owner is the only account that exists at first;
  # it creates every other staff/admin account afterward from the admin panel.
  if [ -n "${OWNER_USERNAME:-}" ] && [ -n "${OWNER_PASSWORD:-}" ]; then
    ENTERED_OWNER_USERNAME="$OWNER_USERNAME"
    ENTERED_OWNER_PASSWORD="$OWNER_PASSWORD"
  elif [ -r /dev/tty ]; then
    echo
    log "Set up the owner account (the first admin login)."
    read -r -p "Admin username: " ENTERED_OWNER_USERNAME </dev/tty
    while true; do
      read -r -s -p "Admin password: " ENTERED_OWNER_PASSWORD </dev/tty; echo
      read -r -s -p "Confirm password: " ENTERED_OWNER_PASSWORD_CONFIRM </dev/tty; echo
      [ "$ENTERED_OWNER_PASSWORD" = "$ENTERED_OWNER_PASSWORD_CONFIRM" ] && break
      warn "Passwords didn't match — try again."
    done
  else
    die "No terminal to prompt on and OWNER_USERNAME/OWNER_PASSWORD were not set. Re-run with OWNER_USERNAME=... OWNER_PASSWORD=... or run this script interactively (not piped)."
  fi
  sed -i "s#^PHONIX_OWNER_USERNAME=.*#PHONIX_OWNER_USERNAME=${ENTERED_OWNER_USERNAME}#" .env
  sed -i "s#^PHONIX_OWNER_PASSWORD=.*#PHONIX_OWNER_PASSWORD=${ENTERED_OWNER_PASSWORD}#" .env

  # Cluster mode — optional High Availability (one Primary + one Standby, business continuity only, never
  # required). Pre-set CLUSTER_MODE to skip the prompt; leaving everything unset defaults to Standalone,
  # which is byte-for-byte today's single-server behavior.
  if [ -n "${CLUSTER_MODE:-}" ]; then
    ENTERED_CLUSTER_MODE="$CLUSTER_MODE"
  elif [ -r /dev/tty ]; then
    echo
    log "Cluster mode (press Enter for a normal single-server install):"
    echo "  1) Standalone (default)"
    echo "  2) Primary node"
    echo "  3) Standby node"
    read -r -p "Choice [1]: " CLUSTER_CHOICE </dev/tty
    case "${CLUSTER_CHOICE:-1}" in
      2) ENTERED_CLUSTER_MODE="primary" ;;
      3) ENTERED_CLUSTER_MODE="standby" ;;
      *) ENTERED_CLUSTER_MODE="standalone" ;;
    esac
  else
    ENTERED_CLUSTER_MODE="standalone"
  fi
  sed -i "s#^PHONIX_CLUSTER_MODE=.*#PHONIX_CLUSTER_MODE=${ENTERED_CLUSTER_MODE}#" .env

  if [ "$ENTERED_CLUSTER_MODE" != "standalone" ]; then
    # A short label so the admin panel's Cluster Management page can tell the two servers apart.
    if [ -n "${NODE_ID:-}" ]; then
      ENTERED_NODE_ID="$NODE_ID"
    elif [ -r /dev/tty ]; then
      read -r -p "Node id/label for this server (e.g. germany, iran): " ENTERED_NODE_ID </dev/tty
    else
      ENTERED_NODE_ID=""
    fi
    sed -i "s#^PHONIX_NODE_ID=.*#PHONIX_NODE_ID=${ENTERED_NODE_ID}#" .env

    # Every cluster node needs the other one's reachable base URL to sync against.
    if [ -n "${CLUSTER_PEER:-}" ]; then
      ENTERED_CLUSTER_PEER="$CLUSTER_PEER"
    elif [ -r /dev/tty ]; then
      read -r -p "Peer server URL (e.g. https://other-server:5228): " ENTERED_CLUSTER_PEER </dev/tty
    else
      die "Cluster mode requires CLUSTER_PEER to be set when running non-interactively."
    fi
    sed -i "s#^PHONIX_CLUSTER_PEER=.*#PHONIX_CLUSTER_PEER=${ENTERED_CLUSTER_PEER}#" .env

    # Shared secret authenticating every request between the two nodes. Only the Primary generates one;
    # the Standby must be given that SAME value (copy it from the Primary's setup — it's shown once below
    # and never again, same as PHONIX_BACKUP_KEY's generation on the bare-metal installer).
    if [ -n "${CLUSTER_SECRET:-}" ]; then
      ENTERED_CLUSTER_SECRET="$CLUSTER_SECRET"
    elif [ "$ENTERED_CLUSTER_MODE" = "primary" ]; then
      ENTERED_CLUSTER_SECRET="$(openssl rand -base64 32)"
      warn "Generated PHONIX_CLUSTER_SECRET — copy this to the Standby's setup, it will not be shown again:"
      echo "  $ENTERED_CLUSTER_SECRET"
    elif [ -r /dev/tty ]; then
      read -r -p "Cluster secret (paste the Primary's PHONIX_CLUSTER_SECRET): " ENTERED_CLUSTER_SECRET </dev/tty
    else
      die "Standby mode requires CLUSTER_SECRET to be set when running non-interactively."
    fi
    sed -i "s#^PHONIX_CLUSTER_SECRET=.*#PHONIX_CLUSTER_SECRET=${ENTERED_CLUSTER_SECRET}#" .env
  fi
else
  warn ".env already exists — leaving it untouched (owner credentials, if changed, must be edited there by hand)."
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
if [ -n "${PHONIX_OWNER_USERNAME:-}" ] && [ -n "${PHONIX_OWNER_PASSWORD:-}" ]; then
  log "Admin login is the username you just set. Log in from the admin panel to create any other staff accounts."
else
  warn "PHONIX_OWNER_USERNAME/PHONIX_OWNER_PASSWORD are not set in .env — there is NO admin account yet."
  warn "Set both in .env by hand, then run: docker compose up -d"
fi
