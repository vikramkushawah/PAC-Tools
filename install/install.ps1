<#
.SYNOPSIS
    Builds and installs the PAC Tools Revit add-in for the current Windows user.

.DESCRIPTION
    Checks that Revit is closed and that the .NET SDK and the Revit API are present, builds the add-in,
    copies it into the per-user Revit add-ins folder, and hash-verifies what it installed against what it
    built. Every failure says what to do about it rather than just what went wrong.

    Installs to:
        %AppData%\Autodesk\Revit\<version>\PacTools.addin      the manifest Revit reads
        %AppData%\Autodesk\Revit\<version>\PacTools\           the add-in itself

    Nothing is written outside your own user profile, so no administrator rights are needed.

.PARAMETER RevitVersion
    Which Revit to install into. Default 2025.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER RevitInstallDir
    Where Revit is installed, if it is not under C:\Program Files\Autodesk.

.PARAMETER Uninstall
    Removes PAC Tools instead of installing it.

.PARAMETER SkipBuild
    Installs whatever is already in bin, without rebuilding.

.PARAMETER Pull
    Runs "git pull" in the repository first.

.EXAMPLE
    .\install.ps1
.EXAMPLE
    .\install.ps1 -RevitVersion 2026
.EXAMPLE
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string] $RevitVersion = '2025',
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $RevitInstallDir,
    [switch] $Uninstall,
    [switch] $SkipBuild,
    [switch] $Pull
)

$ErrorActionPreference = 'Stop'

# ----------------------------------------------------------------------------- output

function Step { param($m) Write-Host ''; Write-Host "==> $m" -ForegroundColor Cyan }
function Good { param($m) Write-Host "    ok    $m" -ForegroundColor Green }
function Note { param($m) Write-Host "    .     $m" -ForegroundColor Gray }
function Warn { param($m) Write-Host "    warn  $m" -ForegroundColor Yellow }

function Fail {
    param([string] $Problem, [string] $WhatToDo)
    Write-Host ''
    Write-Host "  STOPPED: $Problem" -ForegroundColor Red
    if ($WhatToDo) {
        Write-Host ''
        Write-Host "  What to do:" -ForegroundColor Yellow
        foreach ($line in $WhatToDo -split "`n") { Write-Host "    $line" -ForegroundColor Yellow }
    }
    Write-Host ''
    exit 1
}

# ----------------------------------------------------------------------------- where things are

$repoRoot  = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repoRoot 'src\PacTools\PacTools.csproj'
$manifest  = Join-Path $repoRoot 'PacTools.addin'
$buildDir  = Join-Path $repoRoot "src\PacTools\bin\$Configuration"
$addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$targetDir = Join-Path $addinsDir 'PacTools'

Write-Host ''
Write-Host '  PAC Tools - Revit add-in installer' -ForegroundColor White
Write-Host '  Excel spreadsheets to Revit schedules.  Tandon Consultants Pvt. Ltd.' -ForegroundColor Gray
Write-Host ("  " + ('-' * 68)) -ForegroundColor DarkGray

# ----------------------------------------------------------------------------- uninstall

if ($Uninstall) {
    Step "Removing PAC Tools from Revit $RevitVersion"

    $running = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
    if ($running) {
        Fail "Revit is running." "Close Revit completely, then run this again.`nRevit keeps the add-in files locked while it is open."
    }

    $removed = 0
    $manifestTarget = Join-Path $addinsDir 'PacTools.addin'

    if (Test-Path $manifestTarget) {
        Remove-Item $manifestTarget -Force
        Good 'removed PacTools.addin'
        $removed++
    }

    if (Test-Path $targetDir) {
        $n = (Get-ChildItem $targetDir -File -Recurse -ErrorAction SilentlyContinue).Count
        Remove-Item $targetDir -Recurse -Force
        Good "removed the PacTools folder ($n file(s))"
        $removed += $n
    }

    Write-Host ''
    if ($removed -eq 0) {
        Write-Host "  PAC Tools was not installed for Revit $RevitVersion - nothing to remove." -ForegroundColor Gray
    }
    else {
        Write-Host "  Done. Restart Revit; the PAC Tools tab will be gone." -ForegroundColor Green
        Write-Host '  Your projects are untouched: uninstalling removes the add-in, not any data.' -ForegroundColor Gray
    }
    Write-Host ''
    exit 0
}

# ----------------------------------------------------------------------------- checks

Step 'Checking this machine'

if (-not (Test-Path $project)) {
    Fail "The project was not found at $project" "Run this script from inside the PAC-Tools repository - it expects to sit in the 'install' folder.`nIf you have not cloned it yet:`n  git clone https://github.com/vikramkushawah/PAC-Tools.git"
}
Good "repository: $repoRoot"

$running = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($running) {
    Fail "Revit is running (process $($running.Id -join ', '))." "Close Revit completely, then run this again.`nRevit keeps the add-in DLL locked while it is open, so the files cannot be replaced."
}
Good 'Revit is closed'

# Revit, for the API DLLs the build references.
if (-not $RevitInstallDir) { $RevitInstallDir = "C:\Program Files\Autodesk\Revit $RevitVersion\" }
if (-not $RevitInstallDir.EndsWith('\')) { $RevitInstallDir += '\' }

if (-not (Test-Path (Join-Path $RevitInstallDir 'RevitAPI.dll'))) {
    $found = @(Get-ChildItem 'C:\Program Files\Autodesk' -Directory -Filter 'Revit 20*' -ErrorAction SilentlyContinue |
                  Where-Object { Test-Path (Join-Path $_.FullName 'RevitAPI.dll') } |
                  ForEach-Object { $_.Name })

    $advice = "PAC Tools is built against the Revit API DLLs, so Revit has to be installed on this machine."
    if ($found.Count -gt 0) {
        $advice += "`n`nRevit versions found here: $($found -join ', ')"
        $versions = $found | ForEach-Object { ($_ -split ' ')[1] }
        $advice += "`nTry:  .\install.ps1 -RevitVersion $($versions[0])"
    }
    else {
        $advice += "`n`nNo Revit installation was found under C:\Program Files\Autodesk."
        $advice += "`nIf Revit is installed elsewhere, pass the folder:"
        $advice += "`n  .\install.ps1 -RevitInstallDir ""D:\Autodesk\Revit $RevitVersion\"""
    }
    Fail "RevitAPI.dll was not found in $RevitInstallDir" $advice
}
Good "Revit $RevitVersion API: $RevitInstallDir"

# The .NET SDK. Revit 2025 and 2026 are both .NET 8; a newer SDK can still build a net8.0 target.
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail 'The .NET SDK is not installed (the "dotnet" command was not found).' "Install the .NET 8 SDK - the SDK, not just the runtime:`n  https://dotnet.microsoft.com/download/dotnet/8.0`nThen close and reopen this window and run the installer again."
}

$sdks = @(& dotnet --list-sdks 2>$null)
$majors = @($sdks | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } } | Sort-Object -Unique)
if (($majors | Where-Object { $_ -ge 8 }).Count -eq 0) {
    Fail "No .NET 8 or newer SDK was found (found: $($sdks -join '; '))." "Install the .NET 8 SDK:`n  https://dotnet.microsoft.com/download/dotnet/8.0"
}
Good ".NET SDK $(($majors | Where-Object { $_ -ge 8 } | Select-Object -Last 1)).x present"

if ($Pull) {
    Step 'Updating the repository'
    Push-Location $repoRoot
    try {
        & git pull --ff-only
        if ($LASTEXITCODE -ne 0) { Warn 'git pull did not succeed - carrying on with the files as they are.' }
        else { Good 'up to date' }
    }
    finally { Pop-Location }
}

# ----------------------------------------------------------------------------- build

if ($SkipBuild) {
    Step 'Skipping the build (-SkipBuild)'
}
else {
    Step "Building PAC Tools ($Configuration, Revit $RevitVersion)"
    Note 'The first build downloads NuGet packages and can take a minute.'
    Write-Host ''

    & dotnet build $project -c $Configuration `
        -p:RevitVersion=$RevitVersion `
        -p:RevitInstallDir="$RevitInstallDir" `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Fail 'The build failed - the errors are listed above.' @"
This source has not been compiled before: it was written in a container with no
.NET SDK and no Revit, so the first build is expected to need corrections.

Copy the error lines above (the ones starting with "error CS") and send them back,
and they will be fixed. Nothing has been installed, so Revit is untouched.
"@
    }
    Write-Host ''
    Good 'built with no errors'
}

$dllSource = Join-Path $buildDir 'PacTools.dll'
if (-not (Test-Path $dllSource)) {
    Fail "PacTools.dll was not found in $buildDir" "The build did not produce the add-in.`nRun without -SkipBuild, or build by hand:`n  dotnet build ""$project"" -c $Configuration"
}

# ----------------------------------------------------------------------------- install

Step "Installing into Revit $RevitVersion"

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$copied = 0
foreach ($name in 'PacTools.dll', 'PacTools.pdb', 'PacTools.xml') {
    $src = Join-Path $buildDir $name
    if (-not (Test-Path $src)) {
        if ($name -eq 'PacTools.dll') { Fail "$name is missing from the build output." 'Rebuild without -SkipBuild.' }
        Note "$name was not produced - skipped (it is optional)"
        continue
    }
    Copy-Item $src -Destination $targetDir -Force
    Good "$name -> $targetDir"
    $copied++
}

Copy-Item $manifest -Destination $addinsDir -Force
Good "PacTools.addin -> $addinsDir"
$copied++

# ----------------------------------------------------------------------------- verify

Step 'Verifying the installed files'

$dllTarget = Join-Path $targetDir 'PacTools.dll'
$hashBuilt    = (Get-FileHash $dllSource -Algorithm SHA256).Hash
$hashInstalled = (Get-FileHash $dllTarget -Algorithm SHA256).Hash

if ($hashBuilt -ne $hashInstalled) {
    Fail 'The installed PacTools.dll does not match the one that was just built.' "Something replaced the file mid-install - antivirus, or a sync client such as OneDrive.`nClose Revit, pause file sync for the add-ins folder, and run the installer again."
}
Good "PacTools.dll matches the build (SHA-256 $($hashBuilt.Substring(0, 16))...)"

try {
    $version = [System.Reflection.AssemblyName]::GetAssemblyName($dllTarget).Version
    Good "version $version"
}
catch { Note 'could not read the assembly version (not a problem)' }

# The manifest must point at where the DLL actually went, or Revit silently loads nothing.
[xml] $addinXml = Get-Content (Join-Path $addinsDir 'PacTools.addin')
$assemblyPath = $addinXml.RevitAddIns.AddIn.Assembly
$expected = Join-Path $addinsDir $assemblyPath
if (-not (Test-Path $expected)) {
    Fail "The manifest points at '$assemblyPath', but there is no file there." 'The manifest and the DLL folder have got out of step. Re-run the installer; if it persists, report it.'
}
Good "the manifest points at the installed DLL ($assemblyPath)"
Good "add-in id $($addinXml.RevitAddIns.AddIn.AddInId)"

# ----------------------------------------------------------------------------- done

Write-Host ''
Write-Host ("  " + ('-' * 68)) -ForegroundColor DarkGray
Write-Host "  Installed - $copied file(s) into $addinsDir" -ForegroundColor Green
Write-Host ''
Write-Host '  Next:' -ForegroundColor White
Write-Host '    1. Start Revit and open any project.' -ForegroundColor Gray
Write-Host '    2. Look for a new PAC TOOLS tab on the ribbon, beside TCPL BIM Tools.' -ForegroundColor Gray
Write-Host '    3. It has two panels: Model (Element Count) and Help (About).' -ForegroundColor Gray
Write-Host ''
Write-Host '  If the tab does not appear, Revit will have logged why here:' -ForegroundColor Gray
Write-Host "    $env:LOCALAPPDATA\PACTools\logs\pactools.log" -ForegroundColor DarkGray
Write-Host '  Revit may also show a one-time "unknown add-in" prompt - choose Always Load.' -ForegroundColor Gray
Write-Host ''
Write-Host '  To remove it again:  Uninstall-PACTools.bat' -ForegroundColor Gray
Write-Host ''
