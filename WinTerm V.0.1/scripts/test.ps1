<#
.SYNOPSIS
  Builds and runs the NIXUE CMD test suites using the in-box C# compiler.

.DESCRIPTION
  Runs two suites:
    1. Unit tests  (Nixue.Tests.exe)       - deterministic, offline, pure logic.
    2. Integration tests (Nixue.IntegrationTests.exe) - spawns real shells via
       ConPTY. Built as a winexe (no console subsystem) so the child shells inherit a
       real pseudoconsole, matching the GUI app's host context; results are written to
       a file this script reads.

  Exit code is non-zero if any suite fails.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root "build"

# Ensure a fresh build first.
& (Join-Path $PSScriptRoot "build.ps1") -Configuration Debug | Out-Null

$fwRoot = "C:\Windows\Microsoft.NET\Framework64"
$rt = (Get-ChildItem $fwRoot -Directory -Filter "v4.*" | Sort-Object Name -Descending | Select-Object -First 1).FullName
$csc = Join-Path $rt "csc.exe"
$noBom = New-Object System.Text.UTF8Encoding($false)
$core = Join-Path $buildDir "Nixue.Core.dll"
$app  = Join-Path $buildDir "Nixue.exe"

function Compile([string]$rspName, [System.Collections.Generic.List[string]]$argList) {
    $rsp = Join-Path $env:TEMP $rspName
    [System.IO.File]::WriteAllLines($rsp, [string[]]$argList, $noBom)
    & $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Compile failed: $rspName" }
}

$overall = 0

# ---- Unit tests ----------------------------------------------------------
Write-Host "=== Unit tests ===" -ForegroundColor Cyan
$utOut = Join-Path $buildDir "Nixue.Tests.exe"
$u = New-Object System.Collections.Generic.List[string]
"/target:exe","/out:`"$utOut`"","/nologo","/langversion:5","/platform:x64","/warn:3" | ForEach-Object { $u.Add($_) }
$u.Add("/r:`"$core`""); $u.Add("/r:$rt\System.dll"); $u.Add("/r:$rt\System.Core.dll")
Get-ChildItem -Recurse (Join-Path $root "tests\Nixue.Tests") -Filter *.cs | ForEach-Object { $u.Add("`"$($_.FullName)`"") }
Compile "ut.rsp" $u
& $utOut
if ($LASTEXITCODE -ne 0) { $overall = 1 }

# ---- Integration tests ---------------------------------------------------
Write-Host ""
Write-Host "=== Integration tests (real shells via ConPTY) ===" -ForegroundColor Cyan
$itOut = Join-Path $buildDir "Nixue.IntegrationTests.exe"
$i = New-Object System.Collections.Generic.List[string]
"/target:winexe","/out:`"$itOut`"","/nologo","/langversion:5","/platform:x64","/warn:3" | ForEach-Object { $i.Add($_) }
$i.Add("/r:`"$core`""); $i.Add("/r:`"$app`""); $i.Add("/r:$rt\System.dll"); $i.Add("/r:$rt\System.Core.dll")
Get-ChildItem -Recurse (Join-Path $root "tests\Nixue.IntegrationTests") -Filter *.cs | ForEach-Object { $i.Add("`"$($_.FullName)`"") }
Compile "it.rsp" $i

$results = Join-Path $buildDir "integration-results.txt"
if (Test-Path $results) { Remove-Item $results }
$proc = Start-Process -FilePath $itOut -ArgumentList "`"$results`"" -PassThru -Wait
if (Test-Path $results) { Get-Content $results | Write-Host }
else { Write-Host "No integration results produced." -ForegroundColor Yellow }
if ($proc.ExitCode -ne 0) { $overall = 1 }

Write-Host ""
if ($overall -eq 0) { Write-Host "ALL SUITES PASSED" -ForegroundColor Green }
else { Write-Host "SOME TESTS FAILED" -ForegroundColor Red }
exit $overall
