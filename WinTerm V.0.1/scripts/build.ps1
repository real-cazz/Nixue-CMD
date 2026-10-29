<#
.SYNOPSIS
  Builds NIXUE CMD using the in-box .NET Framework C# compiler (csc.exe).

.DESCRIPTION
  No .NET SDK, MSBuild or Visual Studio is required. The app is a pure-C# (no XAML)
  WPF application targeting .NET Framework 4.x, so csc.exe compiles it directly. WPF
  reference assemblies are resolved from the GAC.

.PARAMETER Configuration
  Debug (default) or Release. Release enables optimisation and omits the PDB.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Configuration Release
#>
param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root "build"
New-Item -ItemType Directory -Force $buildDir | Out-Null

# ---- Locate the compiler and runtime ------------------------------------
$fwRoot = "C:\Windows\Microsoft.NET\Framework64"
if (-not (Test-Path $fwRoot)) { $fwRoot = "C:\Windows\Microsoft.NET\Framework" }
$rt = Get-ChildItem $fwRoot -Directory -Filter "v4.*" |
      Sort-Object Name -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName }
$csc = Join-Path $rt "csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found. A .NET Framework 4.x installation is required." }
Write-Host "Compiler : $csc"

# ---- Resolve GAC assemblies ---------------------------------------------
function Find-Gac([string]$name) {
    $bases = @(
        "C:\Windows\Microsoft.NET\assembly\GAC_64",
        "C:\Windows\Microsoft.NET\assembly\GAC_MSIL",
        "C:\Windows\Microsoft.NET\assembly\GAC_32"
    )
    foreach ($b in $bases) {
        $hit = Get-ChildItem (Join-Path $b $name) -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "Could not locate $name.dll in the GAC."
}

$presentationCore = Find-Gac "PresentationCore"
$presentationFramework = Find-Gac "PresentationFramework"
$windowsBase = Find-Gac "WindowsBase"
$systemXaml = Find-Gac "System.Xaml"

$noBom = New-Object System.Text.UTF8Encoding($false)
function Invoke-Csc([string]$rspName, [System.Collections.Generic.List[string]]$argList) {
    $rsp = Join-Path $env:TEMP $rspName
    [System.IO.File]::WriteAllLines($rsp, [string[]]$argList, $noBom)
    & $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($rspName), exit $LASTEXITCODE." }
}

$common = New-Object System.Collections.Generic.List[string]
$common.Add("/nologo")
$common.Add("/langversion:5")
$common.Add("/platform:x64")
$common.Add("/warn:3")
if ($Configuration -eq "Release") { $common.Add("/optimize+"); $common.Add("/debug-") }
else { $common.Add("/debug:pdbonly"); $common.Add("/define:DEBUG") }

# ---- Core library --------------------------------------------------------
$coreOut = Join-Path $buildDir "Nixue.Core.dll"
$a = New-Object System.Collections.Generic.List[string]
$a.AddRange($common)
$a.Add("/target:library")
$a.Add("/out:`"$coreOut`"")
$a.Add("/r:$rt\System.dll")
$a.Add("/r:$rt\System.Core.dll")
Get-ChildItem -Recurse (Join-Path $root "src\Nixue.Core") -Filter *.cs |
    ForEach-Object { $a.Add("`"$($_.FullName)`"") }
Write-Host "Building Nixue.Core.dll ($Configuration)..."
Invoke-Csc "at_core.rsp" $a

# ---- Application (WinExe) -------------------------------------------------
$appOut = Join-Path $buildDir "Nixue.exe"
$manifest = Join-Path $root "src\Nixue.App\app.manifest"
$b = New-Object System.Collections.Generic.List[string]
$b.AddRange($common)
$b.Add("/target:winexe")
$b.Add("/out:`"$appOut`"")
$b.Add("/win32manifest:`"$manifest`"")
$b.Add("/r:`"$coreOut`"")
$b.Add("/r:$rt\System.dll")
$b.Add("/r:$rt\System.Core.dll")
$b.Add("/r:$rt\System.Xml.dll")
$b.Add("/r:`"$presentationCore`"")
$b.Add("/r:`"$presentationFramework`"")
$b.Add("/r:`"$windowsBase`"")
$b.Add("/r:`"$systemXaml`"")
Get-ChildItem -Recurse (Join-Path $root "src\Nixue.App") -Filter *.cs |
    ForEach-Object { $b.Add("`"$($_.FullName)`"") }
Write-Host "Building Nixue.exe ($Configuration)..."
Invoke-Csc "at_app.rsp" $b

# ---- winfetch helper command --------------------------------------------
$wfOut = Join-Path $buildDir "winfetch.exe"
$w = New-Object System.Collections.Generic.List[string]
$w.AddRange($common)
$w.Add("/target:exe")
$w.Add("/out:`"$wfOut`"")
$w.Add("/r:$rt\System.dll")
Get-ChildItem -Recurse (Join-Path $root "src\Nixue.WinFetch") -Filter *.cs |
    ForEach-Object { $w.Add("`"$($_.FullName)`"") }
Write-Host "Building winfetch.exe ($Configuration)..."
Invoke-Csc "at_winfetch.rsp" $w

Write-Host ""
Write-Host "Build succeeded ($Configuration)." -ForegroundColor Green
Write-Host "  Core     : $coreOut"
Write-Host "  App      : $appOut"
Write-Host "  winfetch : $wfOut"
