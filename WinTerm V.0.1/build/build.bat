@echo off
REM ===========================================================================
REM  NIXUE CMD - clean rebuild
REM  Deletes every regenerable build artifact in this folder (everything here is
REM  produced by the build), removes any stale/duplicate copies, then rebuilds a
REM  fresh Release. Run by double-clicking or from a terminal.
REM ===========================================================================
setlocal EnableExtensions

set "BUILDDIR=%~dp0"
set "ROOT=%~dp0.."

echo.
echo === NIXUE CMD clean rebuild ===
echo Cleaning regenerable artifacts in "%BUILDDIR%" ...

REM Delete all files in the build folder except this script.
pushd "%BUILDDIR%"
for %%F in (*) do (
    if /I not "%%~nxF"=="build.bat" del /q "%%F" 2>nul
)
REM Delete any sub-folders (e.g. _diag) that the build does not need.
for /d %%D in (*) do rmdir /s /q "%%D" 2>nul
popd

echo Rebuilding (Release) ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\scripts\build.ps1" -Configuration Release
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
    echo Clean rebuild complete. Output is in "%BUILDDIR%".
) else (
    echo Build FAILED with exit code %RC%.
)

endlocal & exit /b %RC%
