#!/usr/bin/env bash
set -euo pipefail

REPO_URL="https://github.com/AbolfazlTafakori/Phonix.git"
REPO_DIR="/opt/phoenix/repo"

C_RESET="\033[0m"; C_BOLD="\033[1m"; C_BLUE="\033[1;34m"; C_GREEN="\033[1;32m"; C_RED="\033[1;31m"

if [[ $EUID -ne 0 ]]; then
    printf "%b\n" "${C_RED}✖ این دستور باید با دسترسی root اجرا شود:${C_RESET}"
    printf "%b\n" "  ${C_BOLD}sudo bash <(curl -Ls https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh)${C_RESET}"
    exit 1
fi

if [[ ! -t 0 ]]; then
    printf "%b\n" "${C_RED}✖ ورودی تعاملی در دسترس نیست.${C_RESET}"
    printf "%b\n" "  از این فرم استفاده کنید (نه curl | bash):"
    printf "%b\n" "  ${C_BOLD}sudo bash <(curl -Ls https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh)${C_RESET}"
    exit 1
fi

printf "%b\n" "${C_BLUE}:: آماده‌سازی نصب‌کننده‌ی Phoenix Store...${C_RESET}"

export DEBIAN_FRONTEND=noninteractive
if ! command -v git >/dev/null 2>&1; then
    apt-get update -y
    apt-get install -y git
fi

mkdir -p "$(dirname "$REPO_DIR")"
if [[ -d "$REPO_DIR/.git" ]]; then
    git -C "$REPO_DIR" fetch --all --prune
    git -C "$REPO_DIR" reset --hard origin/main
else
    git clone "$REPO_URL" "$REPO_DIR"
fi

printf "%b\n" "${C_GREEN}✔ نصب‌کننده دریافت شد. اجرای فرایند نصب...${C_RESET}\n"
exec bash "$REPO_DIR/deploy/install.sh"
