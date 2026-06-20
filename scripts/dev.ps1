# Local development runner (Windows). Opens the API and the storefront in two terminals,
# in the right order, so you only run one command.  Usage:  ./scripts/dev.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Starting Phonix backend (:5228) and frontend (:3000) in separate windows..." -ForegroundColor Cyan

Start-Process powershell -WorkingDirectory "$root\backend\src\Phonix.Api" `
  -ArgumentList '-NoExit', '-Command', 'Write-Host "PHONIX API :5228" -ForegroundColor Green; dotnet run'

Start-Process powershell -WorkingDirectory "$root\frontend" `
  -ArgumentList '-NoExit', '-Command', 'Write-Host "PHONIX WEB :3000" -ForegroundColor Green; npm run dev'

Write-Host "Backend  -> http://localhost:5228" -ForegroundColor Green
Write-Host "Frontend -> http://localhost:3000" -ForegroundColor Green
Write-Host "Each runs in its own window; close the window or press Ctrl+C there to stop it." -ForegroundColor Yellow
