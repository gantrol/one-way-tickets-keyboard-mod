param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\$([char]0x518D)$([char]0x518D)$([char]0x542F)$([char]0x7A0B) One Way Tickets",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$ManagedDir = Join-Path $GameRoot "One Way Tickets_Data\Managed"

if (-not (Test-Path $ManagedDir)) {
    throw "Game Managed directory not found: $ManagedDir"
}

if (-not $SkipBuild) {
    Write-Host "[1/4] Building OneWayTickets.KeyboardMod.csproj ..." -ForegroundColor Cyan
    dotnet build (Join-Path $ProjectDir "OneWayTickets.KeyboardMod.csproj") -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

$BuiltDll = Join-Path $ProjectDir "bin\Release\OneWayTickets.KeyboardMod.dll"
if (-not (Test-Path $BuiltDll)) {
    throw "Build artifact not found: $BuiltDll"
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
    "C:\Users\gantrol\.nuget\packages\microsoft.net.illink.tasks\9.0.18\tools\net472\Mono.Cecil.dll"
)
$CecilPath = $CecilCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $CecilPath) {
    $FoundCecil = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages" -Recurse -Filter "Mono.Cecil.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($FoundCecil) { $CecilPath = $FoundCecil.FullName }
}
if (-not $CecilPath) {
    throw "Mono.Cecil.dll not found."
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
$TargetIni = Join-Path $GameRoot "OneWayTickets.KeyboardMod.ini"
if ((Test-Path $DefaultIniSrc) -and (-not (Test-Path $TargetIni))) {
    Copy-Item -Path $DefaultIniSrc -Destination $TargetIni -Force
}

Write-Host "[OK] OneWayTickets.KeyboardMod installed successfully!" -ForegroundColor Green
Write-Host "  - Mod DLL: $TargetModDll" -ForegroundColor Gray
Write-Host "  - Backup : $WorldBak" -ForegroundColor Gray
Write-Host "  - Config : $TargetIni" -ForegroundColor Gray
