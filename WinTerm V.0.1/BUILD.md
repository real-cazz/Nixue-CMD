# Building NIXUE CMD

Nixue CMD is deliberately buildable with **only what ships in Windows**. There is
no dependency on the .NET SDK, Visual Studio, MSBuild, NuGet, or network access.

## Requirements

| Requirement | Notes |
| --- | --- |
| Windows 10 1809+ or Windows 11 | ConPTY is needed at runtime, not to build. |
| .NET Framework 4.x | Ships in-box on Windows 10/11. Provides `csc.exe` and WPF. |
| PowerShell 5.1 (Windows PowerShell) | Used only to drive the build scripts. |

The build uses the in-box compiler at
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (the scripts locate it
automatically) and references the WPF assemblies (`PresentationCore`,
`PresentationFramework`, `WindowsBase`, `System.Xaml`) directly from the GAC.

> The in-box compiler supports **C# 5**, so the source is written to that language
> level. This is why the code avoids C# 6+ conveniences (string interpolation,
> null-conditional operators, expression-bodied members, etc.).

## Commands

From the project root:

```powershell
# Debug build (default)
powershell -File scripts\build.ps1

# Release build (optimised, no PDB)
powershell -File scripts\build.ps1 -Configuration Release

# Build + run all tests
powershell -File scripts\test.ps1

# Produce a portable release (folder + zip under dist\)
powershell -File scripts\publish.ps1
```

If your execution policy blocks scripts, run them in an existing PowerShell session
with the call operator instead of spawning a new shell:

```powershell
& .\scripts\build.ps1 -Configuration Release
```

## Outputs

| Path | Description |
| --- | --- |
| `build\Nixue.exe` | The application (WinExe, x64). |
| `build\Nixue.Core.dll` | WPF-free core library (parser, config, themes). |
| `build\Nixue.Tests.exe` | Unit-test runner (console). |
| `build\Nixue.IntegrationTests.exe` | Integration-test runner (winexe). |
| `dist\Nixue-<version>-portable\` | Staged portable release. |
| `dist\Nixue-<version>-portable.zip` | Zipped portable release. |

## How the build works

1. `build.ps1` locates `csc.exe` and the GAC WPF assemblies.
2. It compiles `src\Nixue.Core\**` into `Nixue.Core.dll`
   (`/target:library`, references only `System.dll` / `System.Core.dll`).
3. It compiles `src\Nixue.App\**` into `Nixue.exe`
   (`/target:winexe`, `/platform:x64`, `/win32manifest:app.manifest`, referencing the
   core DLL and the WPF assemblies).
4. Compiler arguments are written to a BOM-free response file to avoid command-line
   length limits.

## Troubleshooting

- **`csc.exe not found`** — the machine is missing .NET Framework 4.x. Install the
  Windows feature "`.NET Framework 4.8 Advanced Services`" (it is present by default on
  Windows 10/11).
- **`Could not locate PresentationCore.dll in the GAC`** — WPF is not installed. WPF
  ships with the Windows desktop .NET Framework; enable the ".NET Framework" Windows
  feature.
- **A compile error mentioning a C# 6+ feature** — the in-box compiler is C# 5 only.
  Rewrite the construct to C# 5 (e.g. `string.Format` instead of `$"..."`).
- **The app won't start and reports an unsupported Windows version** — ConPTY requires
  Windows 10 build 17763 or newer.
