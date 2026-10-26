param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\$([char]0x518D)$([char]0x518D)$([char]0x542F)$([char]0x7A0B) One Way Tickets"
)

$ErrorActionPreference = "Stop"
$ManagedDir = Join-Path $GameRoot "One Way Tickets_Data\Managed"
$WorldDll = Join-Path $ManagedDir "World.dll"
$WorldBak = Join-Path $ManagedDir "World.dll.bak"
$TargetModDll = Join-Path $ManagedDir "OneWayTickets.KeyboardMod.dll"

if (Test-Path $WorldBak) {
    Copy-Item -Path $WorldBak -Destination $WorldDll -Force
    Remove-Item -Path $WorldBak -Force
    Write-Host "[OK] Restored original World.dll from World.dll.bak" -ForegroundColor Green
} else {
    Write-Host "[INFO] World.dll.bak not found, skipping World.dll restore." -ForegroundColor Yellow
}

if (Test-Path $TargetModDll) {
    Remove-Item -Path $TargetModDll -Force
    Write-Host "[OK] Removed OneWayTickets.KeyboardMod.dll" -ForegroundColor Green
}

Write-Host "Uninstall complete." -ForegroundColor Cyan
