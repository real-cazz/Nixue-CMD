<#
.SYNOPSIS
  Produces a portable NIXUE CMD release (a folder plus a .zip).

.DESCRIPTION
  Builds the Release configuration, stages the runnable files and documentation into
  dist\Nixue-<version>-portable, and zips it. The portable build requires only
  the in-box .NET Framework 4.x runtime present on Windows 10/11; nothing is installed.
#>
param(
    [string]$Version = "0.1.0"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root "build"

# Release build.
& (Join-Path $PSScriptRoot "build.ps1") -Configuration Release

$dist = Join-Path $root "dist"
$stageName = "Nixue-$Version-portable"
$stage = Join-Path $dist $stageName
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $buildDir "Nixue.exe") $stage
Copy-Item (Join-Path $buildDir "Nixue.Core.dll") $stage
Copy-Item (Join-Path $buildDir "winfetch.exe") $stage
if (Test-Path (Join-Path $root "README.md"))  { Copy-Item (Join-Path $root "README.md") $stage }
if (Test-Path (Join-Path $root "LICENSE"))    { Copy-Item (Join-Path $root "LICENSE") $stage }
if (Test-Path (Join-Path $root "docs"))       { Copy-Item (Join-Path $root "docs") (Join-Path $stage "docs") -Recurse }

# A tiny launcher note.
$runNote = @"
NIXUE CMD $Version (portable)

Run Nixue.exe. No installation is required.

Requires Windows 10 (build 17763 / version 1809) or newer with the in-box
.NET Framework 4.x runtime (present by default on Windows 10 and 11).

Settings and logs are stored per-user at:
  Documents\Nixue

This folder can be copied or moved anywhere; it is fully self-contained apart
from the operating-system runtime.
"@
[System.IO.File]::WriteAllText((Join-Path $stage "RUN.txt"), $runNote, (New-Object System.Text.UTF8Encoding($false)))

$zip = Join-Path $dist ("$stageName.zip")
if (Test-Path $zip) { Remove-Item -Force $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)

Write-Host ""
Write-Host "Portable release created:" -ForegroundColor Green
Write-Host "  Folder : $stage"
Write-Host "  Zip    : $zip"
Get-ChildItem $stage | Select-Object Name, Length | Format-Table | Out-String | Write-Host
