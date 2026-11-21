param(
    [string]$OutDir = "workshop\upload\TarotSatchel"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "TarotSatchel\bin\Release"
$dst = Join-Path $root $OutDir
$content = Join-Path $dst "Content"

if (-not (Test-Path (Join-Path $build "TarotSatchel.dll"))) {
    throw "TarotSatchel.dll not found in $build -- run 'dotnet build -c Release' in TarotSatchel first."
}

New-Item -ItemType Directory -Force $content | Out-Null
Copy-Item (Join-Path $build "TarotSatchel.dll") $content -Force
Copy-Item (Join-Path $root "TarotSatchel\TarotSatchel.json") $content -Force

# Preview image must be named image.png and stay below 1 MB (Steam limit).
$icon = Join-Path $root "workshop\icon.png"
if ((Get-Item $icon).Length -ge 1MB) { throw "workshop\icon.png is over 1 MB" }
Copy-Item $icon (Join-Path $dst "image.png") -Force

# workshop.json is only created on the first run; later edits (tags, mod_id.txt) are preserved.
$workspaceJson = Join-Path $dst "workshop.json"
if (-not (Test-Path $workspaceJson)) {
    Copy-Item (Join-Path $root "workshop\upload\workshop.template.json") $workspaceJson
    Write-Host "Created workshop.json from template."
}

# Keep the published item id outside the generated (gitignored) workspace so updates survive a clean
# checkout: after the first upload copy workshop\upload\TarotSatchel\mod_id.txt to workshop\mod_id.txt.
$trackedId = Join-Path $root "workshop\mod_id.txt"
$workspaceId = Join-Path $dst "mod_id.txt"
if ((Test-Path $trackedId) -and -not (Test-Path $workspaceId)) {
    Copy-Item $trackedId $workspaceId -Force
    Write-Host "Restored mod_id.txt from workshop\mod_id.txt"
}

Write-Host ""
Write-Host "Workspace ready: $dst"
Write-Host "Next steps:"
Write-Host "  1. Download ModUploader.exe from https://github.com/megacrit/sts2-mod-uploader/releases"
Write-Host "  2. Put ModUploader.exe next to this workspace folder (in workshop\upload)"
Write-Host "  3. Fill in 'tags' in $workspaceJson (tags cannot be changed after publish)"
Write-Host "  4. Open a terminal in workshop\upload and run:"
Write-Host "     .\ModUploader.exe upload -w TarotSatchel"
Get-ChildItem $dst | ForEach-Object { "{0}  {1} bytes" -f $_.Name, $_.Length }
