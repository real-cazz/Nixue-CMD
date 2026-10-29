# Changelog

All notable changes to NIXUE CMD are documented here.

## 0.3.0 — 2026-10-27

### Changed

- **Rebranded to NIXUE CMD** — all namespaces, assemblies (`Nixue.exe`,
  `Nixue.Core.dll`), the default theme ("Nixue Dark"), the context-menu entry, the
  per-session environment markers (`NIXUE`, `NIXUE_SHELL`) and all user-facing text
  were renamed; no "Arch" branding remains.
- **Settings, logs and fonts now live under `Documents\Nixue`** (was `%APPDATA%`).
- **winfetch is much faster** (~0.7s, was several seconds). CPU/GPU usage is now
  sampled via the PDH API with a single wildcard GPU query instead of per-instance
  performance counters, and the whole frame is built before the screen is cleared, so
  there is no blank wait.
- **Modern UI restyle** of the settings panel — flat, themed buttons, text boxes,
  check boxes, sliders and combo boxes (implicit styles parsed from XAML at runtime).
- **Icons** in the title bar, tab strip and tabs are larger, bold and filled (Segoe
  Fluent / MDL2 glyphs); the app mark is bolder.

### Added

- **Smooth cursor fade** (cosine easing instead of hard on/off) plus a **misty,
  gradient trail** left behind the cursor while typing.
- **Font picker dropdown** — drop `.ttf`/`.otf` files into `Documents\Nixue\fonts`
  (an "Open fonts folder" button is provided) and select one from the dropdown; it
  becomes the terminal font.
- **Current directory in the prompt** before the `>` (toggleable), for CMD and
  PowerShell.

## 0.2.0 — 2026-10-26

### Added

- **`winfetch` command** shipped alongside the app (`winfetch.exe`) and placed on each
  session's PATH automatically. Clears the screen, draws the Windows logo in binary
  (1s and 0s) at the top, and prints Windows-native system info with **live CPU and GPU
  usage percentages** (e.g. `NVIDIA GeForce RTX 3050 | 2%`). `--watch` refreshes.
- **Directory shown in the prompt** before the `>` (e.g. `C:\Users\cazz > `), for both
  CMD and PowerShell; toggle via the new "Show current directory before the prompt"
  setting.
- **Custom font upload**: choose a `.ttf`/`.otf` in Settings; it is copied to
  `%APPDATA%\Nixue\fonts` and used as the terminal font.
- **"Open with Nixue CMD"** shell integration: register/unregister a per-user
  (HKCU, no admin) right-click entry for all files, folders and folder backgrounds.
  Opening a file starts a terminal in its folder and shows an in-terminal Paste/No
  prompt to optionally paste the file's contents.
- **`build\build.bat`**: one-click clean rebuild that purges all regenerable build
  artifacts and produces a fresh Release.

### Notes

- Sessions prepend the app's own directory to PATH and set `NIXUE` / `NIXUE_SHELL`
  markers; no global PATH, profile or environment is modified.

## 0.1.0 — 2026-10-26

Initial release.

### Added

- **Application**: pure-C# WPF desktop app (no XAML) targeting .NET Framework 4.x,
  built with the in-box C# compiler — no SDK/MSBuild/Visual Studio required.
- **Terminal backend**: Windows pseudoconsole (ConPTY) integration with safe handle
  management, background output reading, process-exit detection and clean disposal
  (no orphaned shells).
- **Shells**: CMD (default), Windows PowerShell and PowerShell 7, discovered from
  well-known locations; the requested shell is never silently replaced.
- **Terminal engine**: VT100/VT220/xterm-subset parser with partial-sequence and
  UTF-8-boundary handling; SGR 16/256/true colour; cursor movement; erase; insert/
  delete chars and lines; scroll regions; alternate screen; bracketed paste; OSC titles;
  DSR/DA replies. Bounded scrollback.
- **UI**: custom Hyprland-inspired chrome with sharp corners; compact tab strip
  with per-tab running status; dominant terminal viewport; optional status bar;
  programmatic `>_` app icon.
- **Tabs**: independent sessions, each with its own process, working directory,
  environment, scrollback and terminal state.
- **Prompt & cursor**: configurable minimal `>` prompt scoped to the app's own session;
  blinking block/beam/underline cursor; hollow cursor when unfocused.
- **Input**: full key mapping including application-cursor-key mode; `Ctrl+C` interrupt;
  function keys; selection, copy, paste with multiline-paste warning and bracketed
  paste; optional copy-on-select; double-click word / triple-click line selection.
- **Scrollback navigation**: mouse wheel and `Shift`+PageUp/PageDown/Home/End; the view
  stays put while reading earlier output.
- **Shortcuts**: new/close/switch tab, copy/paste, font size, clear, shell menu, new
  window, settings, fullscreen.
- **Themes**: Nixue Dark (default), Midnight, Monochrome, High Contrast; ANSI-16 and
  xterm-256 colour support; centralised theme resources.
- **Settings**: compact panel; per-user JSON config with validation, corrupt-file
  recovery and schema versioning; window-bounds persistence with off-screen guard.
- **Diagnostics**: bounded, secret-free file logging (off by default); compatibility
  detection and a diagnostics report; global exception handling.
- **Build & test**: `build.ps1`, `test.ps1`, `publish.ps1`; 36 unit tests and 9
  integration tests (all passing on Windows 11 build 26300).
- **Docs**: README, BUILD, ARCHITECTURE, COMPATIBILITY, TESTING, CHANGELOG, LICENSE.

### Known limitations

- No reflow of wrapped lines on resize.
- Mouse-reporting sequences are parsed but not forwarded to the shell.
- Bold/italic depend on the chosen font providing those faces; `blink` renders
  statically.
- ConPTY-only: Windows 10 1809 (build 17763) or newer is required.
- Verified on Windows 11 (build 26300); other Windows 10/11 builds are expected to work
  but were not individually tested.
