param(
    [string]$GameRoot = ""
)

$ErrorActionPreference = "Stop"
$GameFolderName = "$([char]0x518D)$([char]0x518D)$([char]0x542F)$([char]0x7A0B) One Way Tickets"

function Resolve-GameRoot([string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path (Join-Path $ExplicitPath "One Way Tickets_Data\Managed")) {
            return (Resolve-Path $ExplicitPath).Path
        }
        throw "Specified -GameRoot does not contain 'One Way Tickets_Data\Managed': $ExplicitPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ONE_WAY_TICKETS_DIR)) {
        if (Test-Path (Join-Path $env:ONE_WAY_TICKETS_DIR "One Way Tickets_Data\Managed")) {
            return (Resolve-Path $env:ONE_WAY_TICKETS_DIR).Path
        }
    }

    $SteamLibs = New-Object System.Collections.Generic.List[string]
    $RegPaths = @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam"
    )
    foreach ($rp in $RegPaths) {
        if (Test-Path $rp) {
            $props = Get-ItemProperty -Path $rp -ErrorAction SilentlyContinue
            foreach ($propName in @("SteamPath", "InstallPath")) {
                $val = $props.$propName
                if (-not [string]::IsNullOrWhiteSpace($val)) {
                    $normalized = $val.Replace("/", "\").TrimEnd("\")
                    if ((Test-Path $normalized) -and (-not $SteamLibs.Contains($normalized))) {
                        $SteamLibs.Add($normalized)
                    }
                    $vdfPath = Join-Path $normalized "steamapps\libraryfolders.vdf"
                    if (Test-Path $vdfPath) {
                        $lines = Get-Content -Path $vdfPath -ErrorAction SilentlyContinue
                        foreach ($line in $lines) {
                            if ($line -match '"path"\s+"([^"]+)"') {
                                $libPath = $Matches[1].Replace("\\", "\").TrimEnd("\")
                                if ((Test-Path $libPath) -and (-not $SteamLibs.Contains($libPath))) {
                                    $SteamLibs.Add($libPath)
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    $Drives = Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue
    foreach ($d in $Drives) {
        $root = $d.Root
        foreach ($sub in @(
            "SteamLibrary",
            "Steam",
            "Program Files (x86)\Steam",
            "Program Files\Steam",
            "Games\SteamLibrary",
            "Games\Steam"
        )) {
            $cand = Join-Path $root $sub
            if ((Test-Path $cand) -and (-not $SteamLibs.Contains($cand))) {
                $SteamLibs.Add($cand)
            }
        }
    }

    foreach ($lib in $SteamLibs) {
        $commonDir = Join-Path $lib "steamapps\common"
        if (-not (Test-Path $commonDir)) { continue }

        $directCandidate = Join-Path $commonDir $GameFolderName
        if (Test-Path (Join-Path $directCandidate "One Way Tickets_Data\Managed")) {
            return (Resolve-Path $directCandidate).Path
        }

        $wildCandidates = Get-ChildItem -Path $commonDir -Directory -Filter "*One Way Tickets*" -ErrorAction SilentlyContinue
        foreach ($wc in $wildCandidates) {
            if (Test-Path (Join-Path $wc.FullName "One Way Tickets_Data\Managed")) {
                return $wc.FullName
            }
        }
    }

    # 4. If not auto-detected, pop up a native Windows Folder Browser Dialog for the user
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "请选择《再再启程 One Way Tickets》游戏安装目录 (Select Game Root Folder)"
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $picked = $dialog.SelectedPath
        if (Test-Path (Join-Path $picked "One Way Tickets_Data\Managed")) {
            return $picked
        }
    }

    throw "Could not find 'One Way Tickets' installation directory."
}

$ResolvedGameRoot = Resolve-GameRoot $GameRoot
$ManagedDir = Join-Path $ResolvedGameRoot "One Way Tickets_Data\Managed"
Write-Host "[0/2] Detected Game Root: $ResolvedGameRoot" -ForegroundColor Green

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
