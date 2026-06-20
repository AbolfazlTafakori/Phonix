#!/usr/bin/env bash
# Local development runner (Linux / macOS / Git Bash). Starts the API then the storefront
# together; Ctrl+C stops both.  Usage:  bash scripts/dev.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "Starting Phonix backend (:5228) and frontend (:3000)... Ctrl+C stops both."
( cd "$ROOT/backend/src/Phonix.Api" && dotnet run ) &
BACK=$!
( cd "$ROOT/frontend" && npm run dev ) &
FRONT=$!

trap 'kill "$BACK" "$FRONT" 2>/dev/null || true' EXIT INT TERM
wait
