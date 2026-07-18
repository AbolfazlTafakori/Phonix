#!/usr/bin/env bash
set -euo pipefail

REPO_URL="https://github.com/AbolfazlTafakori/Phonix.git"
SRC_TARBALL_URL="https://codeload.github.com/AbolfazlTafakori/Phonix/tar.gz/refs/heads/main"
REPO_DIR="/opt/phoenix/repo"

C_RESET="\033[0m"; C_BOLD="\033[1m"; C_BLUE="\033[1;34m"; C_GREEN="\033[1;32m"; C_RED="\033[1;31m"

if [[ $EUID -ne 0 ]]; then
    printf "%b\n" "${C_RED}✖ This installer must run as root:${C_RESET}"
    printf "%b\n" "  ${C_BOLD}curl -fsSLo phoenix-install.sh https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh${C_RESET}"
    printf "%b\n" "  ${C_BOLD}sudo bash phoenix-install.sh${C_RESET}"
    exit 1
fi

if [[ ! -t 0 ]]; then
    printf "%b\n" "${C_RED}✖ No interactive terminal detected.${C_RESET}"
    printf "%b\n" "  Use the download-then-run form (not curl | bash):"
    printf "%b\n" "  ${C_BOLD}curl -fsSLo phoenix-install.sh https://raw.githubusercontent.com/AbolfazlTafakori/Phonix/main/install.sh${C_RESET}"
    printf "%b\n" "  ${C_BOLD}sudo bash phoenix-install.sh${C_RESET}"
    exit 1
fi

printf "%b\n" "${C_BLUE}:: Preparing the Phoenix Store installer...${C_RESET}"

export DEBIAN_FRONTEND=noninteractive
if ! command -v git >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1 || ! command -v tar >/dev/null 2>&1; then
    apt-get update -y
    apt-get install -y git curl tar ca-certificates
fi

mkdir -p "$(dirname "$REPO_DIR")"
# A prior install chowns $REPO_DIR to the service user, so root-run git would abort with
# "dubious ownership". Mark it trusted before touching it.
git config --global --add safe.directory "$REPO_DIR"

# Downloads the source as an ordinary HTTPS archive. Some networks pass normal web traffic but cut
# git's long-lived transfer, so a clone times out where a plain download still succeeds. Extracted to
# a temporary directory and only moved into place once it looks like the repo, so a truncated
# download never replaces a working checkout.
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
    if [[ ! -d "$dir/backend" || ! -d "$dir/frontend" || ! -f "$dir/deploy/install.sh" ]]; then
        rm -rf "$tmp"; return 1
    fi
    rm -rf "$REPO_DIR"
    mkdir -p "$(dirname "$REPO_DIR")"
    mv "$dir" "$REPO_DIR"
    rm -rf "$tmp"
    return 0
}

if [[ -d "$REPO_DIR/.git" ]] \
    && git -C "$REPO_DIR" fetch --all --prune \
    && git -C "$REPO_DIR" reset --hard origin/main; then
    :
elif git clone "$REPO_URL" "$REPO_DIR" 2>/dev/null; then
    :
elif printf "%b\n" "${C_BLUE}:: git is unreachable — downloading the source archive over HTTPS...${C_RESET}" \
    && fetch_source_archive; then
    :
elif [[ -f "$REPO_DIR/deploy/install.sh" ]]; then
    printf "%b\n" "${C_BLUE}:: Could not reach GitHub — using the source already in $REPO_DIR${C_RESET}"
else
    printf "%b\n" "${C_RED}✖ Could not reach GitHub over git or HTTPS, and no local source is available.${C_RESET}"
    printf "%b\n" "  Download the repository archive on another machine, copy it over, extract it,"
    printf "%b\n" "  and run ${C_BOLD}deploy/install.sh${C_RESET} from inside the extracted folder."
    exit 1
fi

printf "%b\n" "${C_GREEN}✔ Installer fetched. Starting installation...${C_RESET}\n"
exec bash "$REPO_DIR/deploy/install.sh"
