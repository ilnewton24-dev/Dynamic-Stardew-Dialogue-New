<#
.SYNOPSIS
    Builds the Living Lore Dialogue SMAPI mod and copies only the required runtime
    files into your local Stardew Valley Mods folder.

.DESCRIPTION
    This is a personal/dev convenience packager. It does NOT copy source files, .git,
    .claude, obj/bin source trees, or the web dashboard source into the Mods folder.
    The web dashboard remains a separate localhost app; the in-game mod talks to it
    through the LocalWebApiBaseUrl setting in config.json.

    Two modes are supported (pass exactly one):
      * Direct Mods install  (-StardewModsFolder) copies into your Stardew Valley Mods folder.
      * Portable package      (-OutputFolder)      copies into any folder, with no Stardew
                                                   Valley install, game folder, or Mods folder
                                                   required.
    In both modes the packaged mod lands in a "<target>\Living Lore Dialogue" subfolder.

.PARAMETER StardewModsFolder
    Direct Mods install mode. Path to your Stardew Valley "Mods" folder (must exist).
    Example: "C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods"

.PARAMETER OutputFolder
    Portable package mode. Any destination folder; created if it does not exist. No Stardew
    Valley installation is required or validated.
    Example: "$env:USERPROFILE\Documents\LivingLoreBuild"

.PARAMETER ResetDatabase
    Overwrite an existing ValleyLedger.db in the destination. Without this flag an
    existing database is preserved.

.PARAMETER ResetConfig
    Overwrite an existing config.json in the destination. Without this flag an existing
    config.json is preserved.

.PARAMETER GamePath
    Optional Stardew Valley install path, forwarded to the build if the SMAPI build
    package cannot auto-detect your game.

.EXAMPLE
    ./package-local-mod.ps1 -OutputFolder "$env:USERPROFILE\Documents\LivingLoreBuild"

.EXAMPLE
    ./package-local-mod.ps1 -StardewModsFolder "C:\path\to\Stardew Valley\Mods"

.EXAMPLE
    ./package-local-mod.ps1 -StardewModsFolder "C:\...\Mods" -ResetDatabase -ResetConfig
#>
[CmdletBinding()]
param(
    [string]$StardewModsFolder,

    [string]$OutputFolder,

    [switch]$ResetDatabase,

    [switch]$ResetConfig,

    [string]$Configuration = "Release",

    [string]$RuntimeIdentifier = "win-x64",

    [string]$GamePath
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "LivingLoreDialogue.csproj"
$webProject = Join-Path $projectRoot "LivingLoreDialogue.Web\LivingLoreDialogue.Web.csproj"
$apiKeyFile = Join-Path $projectRoot "LivingLoreDialogue.Web\openai-api-key.txt"
$modFolderName = "Living Lore Dialogue"
$entryDll = "LivingLoreDialogue.dll"
$sqliteNativeRuntimeIdentifier = "win-x64"
$sqliteNativeRelativePath = "runtimes\$sqliteNativeRuntimeIdentifier\native\e_sqlite3.dll"

# Assemblies that must never be deployed into the Mods folder (provided by the game/SMAPI).
$excludedDlls = @(
    "StardewModdingAPI.dll",
    "StardewValley.dll",
    "Stardew Valley.dll"
)

function Write-Step([string]$message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Write-Ok([string]$message)   { Write-Host "[OK] $message"  -ForegroundColor Green }
function Write-Info([string]$message) { Write-Host "     $message"  -ForegroundColor DarkGray }
function Write-Warn([string]$message) { Write-Host "[WARN] $message" -ForegroundColor Yellow }
function Fail([string]$message) {
    Write-Host "[FAILED] $message" -ForegroundColor Red
    exit 1
}
function Show-Usage {
    Write-Host "Usage: pass exactly one destination mode." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Portable package (no Stardew install required):" -ForegroundColor DarkGray
    Write-Host "    .\package-local-mod.ps1 -OutputFolder ""$env:USERPROFILE\Documents\LivingLoreBuild"""
    Write-Host ""
    Write-Host "  Direct Mods install:" -ForegroundColor DarkGray
    Write-Host "    .\package-local-mod.ps1 -StardewModsFolder ""C:\path\to\Stardew Valley\Mods"""
    Write-Host ""
    Write-Host "  Optional flags: -ResetDatabase  -ResetConfig  -GamePath <path>  -Configuration <name>  -RuntimeIdentifier <rid>"
}

try {
    if (-not (Test-Path $projectFile)) {
        Fail "Could not find the SMAPI project at '$projectFile'. Run this script from the repo root."
    }

    # --- 0. Resolve packaging mode (fail fast before building) -------------------
    $directMode = -not [string]::IsNullOrWhiteSpace($StardewModsFolder)
    $portableMode = -not [string]::IsNullOrWhiteSpace($OutputFolder)

    if ($directMode -and $portableMode) {
        Fail "Pass only one of -StardewModsFolder or -OutputFolder, not both."
    }
    if (-not $directMode -and -not $portableMode) {
        Show-Usage
        exit 1
    }

    $modeLabel = if ($directMode) { "Direct Mods install" } else { "Portable package" }
    Write-Step "Mode: $modeLabel"

    # --- 1. Build the SMAPI mod in Release mode ----------------------------------
    Write-Step "Building $entryDll ($Configuration)..."
    $buildArgs = @(
        "build", $projectFile,
        "-c", $Configuration,
        "-nologo", "-v", "minimal",
        "/p:CopyLocalLockFileAssemblies=true",
        "/p:CopyLocalRuntimeTargetAssets=true",
        "/p:EnableModDeploy=false",
        "/p:EnableModZip=false"
    )
    if ($GamePath) { $buildArgs += "/p:GamePath=$GamePath" }

    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet build failed. If the build can't find your game, re-run with -GamePath ""C:\path\to\Stardew Valley""."
    }
    Write-Ok "Build succeeded."

    # --- 2. Locate the compiled SMAPI output folder ------------------------------
    Write-Step "Locating build output..."
    $buildRoot = Join-Path $projectRoot "bin\$Configuration"
    if (-not (Test-Path $buildRoot)) {
        Fail "Build output folder not found at '$buildRoot'."
    }

    $outputDll = Get-ChildItem -Path $buildRoot -Filter $entryDll -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $outputDll) {
        Fail "Could not find $entryDll under '$buildRoot'."
    }
    $outputDir = $outputDll.Directory.FullName
    Write-Info "Output: $outputDir"

    # --- 3. Create or clean the destination folder -------------------------------
    if ($directMode) {
        # Direct Mods install mode: the Mods folder must already exist.
        if (-not (Test-Path $StardewModsFolder)) {
            Fail "Stardew Mods folder does not exist: '$StardewModsFolder'."
        }
        $baseFolder = $StardewModsFolder
    }
    else {
        # Portable package mode: no Stardew/game/Mods validation. Create the folder if needed.
        if (-not (Test-Path $OutputFolder)) {
            New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
            Write-Info "Created output folder: $OutputFolder"
        }
        $baseFolder = $OutputFolder
    }

    $destination = Join-Path $baseFolder $modFolderName
    $destDb = Join-Path $destination "ValleyLedger.db"
    $destConfig = Join-Path $destination "config.json"

    Write-Step "Preparing destination: $destination"

    # Older SMAPI build-package auto-deploys used the assembly name as the folder name.
    # Remove that generated duplicate only when its manifest matches this mod, otherwise
    # SMAPI may load a stale copy before the curated "Living Lore Dialogue" package.
    $legacyAutoDeployDestination = Join-Path $baseFolder "LivingLoreDialogue"
    if ($legacyAutoDeployDestination -ne $destination -and (Test-Path $legacyAutoDeployDestination)) {
        $legacyManifest = Join-Path $legacyAutoDeployDestination "manifest.json"
        $legacyUniqueId = $null
        if (Test-Path $legacyManifest) {
            try {
                $legacyUniqueId = (Get-Content $legacyManifest -Raw | ConvertFrom-Json).UniqueID
            }
            catch {
                $legacyUniqueId = $null
            }
        }

        if ($legacyUniqueId -eq "fluff.LivingLoreDialogue") {
            Remove-Item $legacyAutoDeployDestination -Recurse -Force
            Write-Info "Removed stale generated package folder: $legacyAutoDeployDestination"
        }
        else {
            Write-Warn "Found '$legacyAutoDeployDestination', but did not remove it because its manifest did not match this mod."
        }
    }

    # Preserve the user's database and config across re-packages unless a reset flag is set.
    $preserveDb = $false
    $preserveConfig = $false
    if (Test-Path $destination) {
        if ((Test-Path $destDb) -and (-not $ResetDatabase)) { $preserveDb = $true }
        if ((Test-Path $destConfig) -and (-not $ResetConfig)) { $preserveConfig = $true }

        if ($preserveDb)     { Write-Info "Preserving existing ValleyLedger.db (use -ResetDatabase to overwrite)." }
        if ($preserveConfig) { Write-Info "Preserving existing config.json (use -ResetConfig to overwrite)." }

        # Remove everything except the files we are preserving.
        Get-ChildItem -Path $destination -Force | ForEach-Object {
            if ($preserveDb -and $_.Name -eq "ValleyLedger.db") { return }
            if ($preserveConfig -and $_.Name -eq "config.json") { return }
            Remove-Item $_.FullName -Recurse -Force
        }
    }
    else {
        New-Item -ItemType Directory -Path $destination | Out-Null
    }

    # --- 4. Copy only the required runtime files ---------------------------------
    Write-Step "Copying runtime files..."

    # Helper: copy a file from the build output (preferred) or repo root fallback.
    function Copy-Required([string]$relativeName, [switch]$optional) {
        $fromOutput = Join-Path $outputDir $relativeName
        $fromRepo = Join-Path $projectRoot $relativeName
        $source = if (Test-Path $fromOutput) { $fromOutput } elseif (Test-Path $fromRepo) { $fromRepo } else { $null }

        if (-not $source) {
            if ($optional) { return $false }
            Fail "Required file missing from build output and repo: $relativeName"
        }

        $target = Join-Path $destination $relativeName
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
        Copy-Item $source $target -Force
        Write-Info "+ $relativeName"
        return $true
    }

    # Entry assembly + manifest.
    Copy-Required $entryDll | Out-Null
    Copy-Required "manifest.json" | Out-Null

    # Lore database SQL.
    Copy-Required "Data\schema.sql" | Out-Null
    Copy-Required "Data\seed.sql" | Out-Null

    # Dependency DLLs from the build output (e.g. Microsoft.Data.Sqlite, SQLitePCLRaw.*),
    # excluding the game/SMAPI assemblies which the host already provides. The build is
    # invoked with CopyLocalLockFileAssemblies=true so NuGet runtime dependencies are copied
    # beside LivingLoreDialogue.dll for SMAPI to resolve.
    $depCount = 0
    Get-ChildItem -Path $outputDir -Filter "*.dll" -File | Sort-Object Name | ForEach-Object {
        if ($_.Name -eq $entryDll) { return }
        if ($excludedDlls -contains $_.Name) { return }
        Copy-Item $_.FullName (Join-Path $destination $_.Name) -Force
        $depCount++
    }
    Write-Info "+ $depCount dependency DLL(s)"

    # Dependency manifest + the one native SQLite runtime library SMAPI needs on Windows x64.
    Get-ChildItem -Path $outputDir -Filter "*.deps.json" -File -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $destination $_.Name) -Force
        Write-Info "+ $($_.Name)"
    }

    $runtimesDir = Join-Path $outputDir "runtimes"
    $nativeSqlite = Join-Path $outputDir $sqliteNativeRelativePath
    if (-not (Test-Path $nativeSqlite)) {
        $availableNativeSqlite = Get-ChildItem -Path $runtimesDir -Filter "e_sqlite3.dll" -Recurse -File -ErrorAction SilentlyContinue |
            ForEach-Object { $_.FullName.Substring($outputDir.Length).TrimStart("\") }
        $availableList = if ($availableNativeSqlite) { $availableNativeSqlite -join ", " } else { "none" }
        Fail "Required Windows x64 SQLite native runtime is missing from build output: $sqliteNativeRelativePath. Available native SQLite files: $availableList"
    }

    $nativeSqliteDestination = Join-Path $destination $sqliteNativeRelativePath
    $nativeSqliteDestinationDir = Split-Path $nativeSqliteDestination -Parent
    if (-not (Test-Path $nativeSqliteDestinationDir)) {
        New-Item -ItemType Directory -Path $nativeSqliteDestinationDir -Force | Out-Null
    }

    Copy-Item $nativeSqlite $nativeSqliteDestination -Force
    Write-Info "+ $sqliteNativeRelativePath"

    # SQLitePCLRaw should resolve through runtimes\win-x64\native, but SMAPI local probing
    # is more forgiving when the same x64 native DLL is beside the mod assembly too.
    Copy-Item $nativeSqlite (Join-Path $destination "e_sqlite3.dll") -Force
    Write-Info "+ e_sqlite3.dll"

    # --- 4b. Publish the dashboard as a self-contained app into Dashboard\ --------
    # Self-contained so the end machine does not need the .NET runtime installed.
    Write-Step "Publishing dashboard (self-contained, $RuntimeIdentifier)..."
    if (-not (Test-Path $webProject)) {
        Fail "Dashboard project not found at '$webProject'."
    }
    $dashboardDir = Join-Path $destination "Dashboard"
    $publishArgs = @(
        "publish", $webProject,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", "true",
        "-o", $dashboardDir,
        "-nologo", "-v", "minimal"
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet publish for the dashboard failed."
    }
    Write-Info "+ Dashboard\ (published self-contained app)"

    # Carry the personal API key file into the dashboard's content root if present.
    if (Test-Path $apiKeyFile) {
        Copy-Item $apiKeyFile (Join-Path $dashboardDir "openai-api-key.txt") -Force
        Write-Info "+ Dashboard\openai-api-key.txt"
    }

    # --- 5 & 6. config.json and ValleyLedger.db with preserve/reset semantics -----
    if ($preserveConfig) {
        Write-Info "config.json left untouched."
    }
    else {
        Copy-Required "config.json" | Out-Null
    }

    if ($preserveDb) {
        Write-Info "ValleyLedger.db left untouched."
    }
    else {
        $repoDb = Join-Path $projectRoot "ValleyLedger.db"
        if (Test-Path $repoDb) {
            Copy-Item $repoDb $destDb -Force
            $verb = if ($ResetDatabase) { "Reset" } else { "Seeded" }
            Write-Info "+ ValleyLedger.db ($verb from repo copy)"
        }
        else {
            Write-Info "No ValleyLedger.db in repo; the mod will create one on first run."
        }
    }

    # --- 7. Validate runtime dependency packaging --------------------------------
    Write-Step "Validating packaged dependencies..."
    $requiredOutputFiles = @(
        "LivingLoreDialogue.dll",
        "Microsoft.Data.Sqlite.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "SQLitePCLRaw.batteries_v2.dll"
    )
    foreach ($fileName in $requiredOutputFiles) {
        $path = Join-Path $destination $fileName
        if (-not (Test-Path $path)) {
            Fail "Packaged mod is missing required runtime file: $fileName"
        }
        Write-Info "Found required file: $fileName"
    }

    $sqliteWarnings = @("SQLitePCLRaw.core.dll")
    foreach ($fileName in $sqliteWarnings) {
        $path = Join-Path $destination $fileName
        if (-not (Test-Path $path)) {
            Write-Warn "Optional SQLite dependency was not found in the mod root: $fileName"
        }
        else {
            Write-Info "Found SQLite dependency: $fileName"
        }
    }

    $rootNativeSqlite = Join-Path $destination "e_sqlite3.dll"
    $runtimeNativeSqlite = Join-Path $destination $sqliteNativeRelativePath
    if (-not (Test-Path $runtimeNativeSqlite)) {
        Fail "Packaged mod is missing required Windows x64 SQLite native runtime: $sqliteNativeRelativePath"
    }
    Write-Info "Found native SQLite dependency: $sqliteNativeRelativePath"

    if (-not (Test-Path $rootNativeSqlite)) {
        Write-Warn "Native SQLite dependency was not copied beside the mod assembly: e_sqlite3.dll"
    }
    else {
        Write-Info "Found native SQLite dependency: e_sqlite3.dll"
    }

    $x86NativeSqlite = Get-ChildItem -Path $destination -Filter "e_sqlite3.dll" -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\runtimes\\win-x86\\native\\e_sqlite3\.dll$" }
    foreach ($file in $x86NativeSqlite) {
        Write-Warn "Unexpected win-x86 SQLite native DLL is present in the packaged mod: $($file.FullName)"
    }

    # --- 8. Report ---------------------------------------------------------------
    Write-Host ""
    Write-Ok "Packaged 'Living Lore Dialogue' successfully."
    Write-Host "Final folder: $destination" -ForegroundColor Yellow
    Write-Host ""
    if ($portableMode) {
        Write-Host "Next: copy the 'Living Lore Dialogue' folder into your Stardew Valley\Mods folder." -ForegroundColor DarkGray
    }
    Write-Host "The dashboard auto-starts from the mod folder on game launch" -ForegroundColor DarkGray
    Write-Host "(EnableLocalDashboardAutoStart in config.json). View it at http://localhost:5077." -ForegroundColor DarkGray
}
catch {
    Fail $_.Exception.Message
}
