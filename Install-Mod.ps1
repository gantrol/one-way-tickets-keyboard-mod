param(
    [string]$GameRoot = "",
    [switch]$SkipBuild,
    [switch]$BuildFromSource
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$GameFolderName = "$([char]0x518D)$([char]0x518D)$([char]0x542F)$([char]0x7A0B) One Way Tickets"

function Resolve-GameRoot([string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path (Join-Path $ExplicitPath "One Way Tickets_Data\Managed\World.dll")) {
            return (Resolve-Path $ExplicitPath).Path
        }
        throw "Specified -GameRoot does not contain 'One Way Tickets_Data\Managed\World.dll': $ExplicitPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ONE_WAY_TICKETS_DIR)) {
        if (Test-Path (Join-Path $env:ONE_WAY_TICKETS_DIR "One Way Tickets_Data\Managed\World.dll")) {
            return (Resolve-Path $env:ONE_WAY_TICKETS_DIR).Path
        }
    }

    $SteamLibs = New-Object System.Collections.Generic.List[string]

    # 1. Read Steam install path from Windows Registry and parse libraryfolders.vdf
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

    # 2. Add common Steam library locations across all local filesystem drives
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

    # 3. Check each library for the game directory
    foreach ($lib in $SteamLibs) {
        $commonDir = Join-Path $lib "steamapps\common"
        if (-not (Test-Path $commonDir)) { continue }

        $directCandidate = Join-Path $commonDir $GameFolderName
        if (Test-Path (Join-Path $directCandidate "One Way Tickets_Data\Managed\World.dll")) {
            return (Resolve-Path $directCandidate).Path
        }

        $wildCandidates = Get-ChildItem -Path $commonDir -Directory -Filter "*One Way Tickets*" -ErrorAction SilentlyContinue
        foreach ($wc in $wildCandidates) {
            if (Test-Path (Join-Path $wc.FullName "One Way Tickets_Data\Managed\World.dll")) {
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
        if (Test-Path (Join-Path $picked "One Way Tickets_Data\Managed\World.dll")) {
            return $picked
        }
    }

    throw "Could not find 'One Way Tickets' installation directory."
}

$ResolvedGameRoot = Resolve-GameRoot $GameRoot
$ManagedDir = Join-Path $ResolvedGameRoot "One Way Tickets_Data\Managed"
Write-Host "[0/4] Detected Game Root: $ResolvedGameRoot" -ForegroundColor Green

$DistDll = Join-Path $ProjectDir "dist\OneWayTickets.KeyboardMod.dll"
$ReleaseDll = Join-Path $ProjectDir "bin\Release\OneWayTickets.KeyboardMod.dll"

if ($BuildFromSource -or ((-not $SkipBuild) -and (-not (Test-Path $DistDll)))) {
    Write-Host "[1/4] Building OneWayTickets.KeyboardMod.csproj ..." -ForegroundColor Cyan
    dotnet build (Join-Path $ProjectDir "OneWayTickets.KeyboardMod.csproj") -c Release "-p:GameManagedDir=$ManagedDir" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
    if (Test-Path $ReleaseDll) {
        New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDir "dist") | Out-Null
        Copy-Item $ReleaseDll $DistDll -Force
    }
}

$BuiltDll = if ((Test-Path $ReleaseDll) -and $BuildFromSource) { $ReleaseDll } elseif (Test-Path $DistDll) { $DistDll } else { $ReleaseDll }
if (-not (Test-Path $BuiltDll)) {
    throw "Mod DLL not found: $BuiltDll"
}

$WorldDll = Join-Path $ManagedDir "World.dll"
$WorldBak = Join-Path $ManagedDir "World.dll.bak"
$TargetModDll = Join-Path $ManagedDir "OneWayTickets.KeyboardMod.dll"

if (-not (Test-Path $WorldBak)) {
    Write-Host "[2/4] Backing up original World.dll -> World.dll.bak ..." -ForegroundColor Cyan
    Copy-Item -Path $WorldDll -Destination $WorldBak -Force
} else {
    Write-Host "[2/4] Found existing clean backup World.dll.bak ..." -ForegroundColor Cyan
}

Write-Host "[3/4] Copying OneWayTickets.KeyboardMod.dll to Managed ..." -ForegroundColor Cyan
Copy-Item -Path $BuiltDll -Destination $TargetModDll -Force

Write-Host "[4/4] Injecting ModEntryPoint.EnsureInitialized() into World.Input.WorldInputController::Update() ..." -ForegroundColor Cyan

$CecilCandidates = @(
    (Join-Path $ProjectDir "dist\Mono.Cecil.dll"),
    "C:\Users\gantrol\.nuget\packages\microsoft.net.illink.tasks\9.0.18\tools\net472\Mono.Cecil.dll"
)
$CecilPath = $CecilCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $CecilPath) {
    $FoundCecil = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages" -Recurse -Filter "Mono.Cecil.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($FoundCecil) { $CecilPath = $FoundCecil.FullName }
}
if (-not $CecilPath) {
    throw "Mono.Cecil.dll not found in dist\ or local NuGet cache."
}

Add-Type -Path $CecilPath

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($ManagedDir)
$readParams = New-Object Mono.Cecil.ReaderParameters
$readParams.AssemblyResolver = $resolver

$worldAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($WorldBak, $readParams)
$modAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($TargetModDll, $readParams)

$entryType = $modAsm.MainModule.GetType("OneWayTickets.KeyboardMod.ModEntryPoint")
$ensureInitMethod = $entryType.Methods | Where-Object { $_.Name -eq "EnsureInitialized" } | Select-Object -First 1
$importedEnsureInit = $worldAsm.MainModule.ImportReference($ensureInitMethod)

$inputCtrlType = $worldAsm.MainModule.GetType("World.Input.WorldInputController")
$updateMethod = $inputCtrlType.Methods | Where-Object { $_.Name -eq "Update" } | Select-Object -First 1

$il = $updateMethod.Body.GetILProcessor()
$firstIns = $updateMethod.Body.Instructions[0]
$callIns = $il.Create([Mono.Cecil.Cil.OpCodes]::Call, $importedEnsureInit)
$il.InsertBefore($firstIns, $callIns)

$TempPatched = Join-Path $ManagedDir "World.dll.patched"
$worldAsm.Write($TempPatched)
$worldAsm.Dispose()
$modAsm.Dispose()

Move-Item -Path $TempPatched -Destination $WorldDll -Force

$DefaultIniSrc = Join-Path $ProjectDir "OneWayTickets.KeyboardMod.ini"
$TargetIni = Join-Path $ResolvedGameRoot "OneWayTickets.KeyboardMod.ini"
if ((Test-Path $DefaultIniSrc) -and (-not (Test-Path $TargetIni))) {
    Copy-Item -Path $DefaultIniSrc -Destination $TargetIni -Force
}

Write-Host "[OK] OneWayTickets.KeyboardMod installed successfully!" -ForegroundColor Green
Write-Host "  - Mod DLL: $TargetModDll" -ForegroundColor Gray
Write-Host "  - Backup : $WorldBak" -ForegroundColor Gray
Write-Host "  - Config : $TargetIni" -ForegroundColor Gray
