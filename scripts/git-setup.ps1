# Initialize the local git repository for the Tarot Satchel mod.
# Run from the repository root:
#   pwsh -File scripts/git-setup.ps1 -UserEmail "sam-wealth@users.noreply.github.com"
#   pwsh -File scripts/git-setup.ps1 -UserEmail "..." -RemoteUrl "git@github.com:sam-wealth/tarot-satchel.git"
param(
    [string]$UserName = "sam-wealth",
    [string]$UserEmail = "sam-wealth@users.noreply.github.com",
    [string]$RemoteUrl = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) {
    Write-Host "git not found. Install Git for Windows first: winget install --id Git.Git -e" -ForegroundColor Yellow
    exit 1
}
Write-Host "git: $(& git --version)"

if (-not (Test-Path (Join-Path $root ".git"))) {
    & git init -b main
    Write-Host "initialized repository (branch main)" -ForegroundColor Green
} else {
    Write-Host "repository already initialized"
}

# Repository-local identity (does not touch global git config).
& git config user.name $UserName
& git config user.email $UserEmail
Write-Host "identity: $UserName <$UserEmail>"

& git add -A
$status = & git status --short
if ($status) {
    & git commit -m "chore: initial commit - Tarot Satchel mod v0.113.1"
    Write-Host "initial commit created" -ForegroundColor Green
} else {
    Write-Host "nothing to commit"
}

if ($RemoteUrl) {
    & git remote remove origin 2>$null
    & git remote add origin $RemoteUrl
    & git branch -M main
    Write-Host "remote set: $RemoteUrl" -ForegroundColor Green
    Write-Host "next: git push -u origin main" -ForegroundColor Cyan
} else {
    Write-Host "next steps (create an empty GitHub repo first):" -ForegroundColor Cyan
    Write-Host "  git remote add origin git@github.com:sam-wealth/tarot-satchel.git"
    Write-Host "  git branch -M main"
    Write-Host "  git push -u origin main"
}
