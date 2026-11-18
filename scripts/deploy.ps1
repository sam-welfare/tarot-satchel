param(
    [string]$GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "TarotSatchel\bin\Release"
$dst = Join-Path $GameDir "mods\TarotSatchel"

if (-not (Test-Path (Join-Path $build "TarotSatchel.dll"))) {
    throw "TarotSatchel.dll not found in $build -- run 'dotnet build -c Release' first."
}

New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path $build "TarotSatchel.dll") $dst -Force
Copy-Item (Join-Path $root "TarotSatchel\TarotSatchel.json") $dst -Force

Write-Host "Deployed TarotSatchel to $dst"
Get-ChildItem $dst | ForEach-Object { "{0}  {1} bytes" -f $_.Name, $_.Length }
