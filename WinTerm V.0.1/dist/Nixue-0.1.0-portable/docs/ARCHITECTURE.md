# Architecture

## Overview

Nixue CMD is split into a WPF-free **Core** library and a WPF **App**:

```
Nixue.Core  (no WPF; unit-testable)
  Terminal/   TermColor, TerminalCell, TerminalBuffer, TerminalEmulator (VT parser)
  Config/     Json, AppSettings, SettingsStore, AppPaths, Theme, ThemeLibrary
  Shells/     ShellInfo, ShellDiscovery
  Diagnostics/ Log, CompatibilityInfo

Nixue.App   (WPF, pure C#, no XAML)
  Interop/    NativeMethods (ConPTY + process P/Invoke)
  Sessions/   ITerminalBackend, ConPtyBackend, TerminalSession, SessionState
  UI/         MainWindow, TerminalView, TerminalTab, SettingsWindow,
              ThemeResources, IconFactory
  Program.cs  entry point (STA), app.manifest (DPI/compat)
```

Keeping the terminal model, VT parser, configuration and theming in a WPF-free library
makes the hardest logic testable without a UI and keeps the UI layer thin.

## Why .NET Framework 4.x WPF, pure C#, built with csc

The target machine had no .NET SDK, MSBuild, or Visual Studio, but did have the in-box
.NET Framework 4.x compiler (`csc.exe`) and the WPF assemblies in the GAC. A pure-C#
(no XAML) WPF app compiles directly with `csc`, needs no markup-compilation step, no
installs, and runs on any Windows 10 1809+/11 out of the box. The trade-off is the
in-box compiler's **C# 5** language level, which the source adheres to.

Alternatives considered and rejected: installing the .NET SDK via winget (requires
network/elevation and violates the offline, no-install goal) and a native C++ build
(no C++ compiler present).

## UI architecture

The entire UI is constructed in code. `MainWindow` uses `WindowChrome` for a custom
title bar while preserving native resize, snap and maximise behaviour, with sharp
corners. The layout is a four-row grid: title bar, tab strip, terminal viewport
(dominant), optional status bar.

`TerminalView` is a custom `FrameworkElement` that renders the active session's grid in
`OnRender`. For each visible row it draws background runs (batched by colour/selection)
then text runs (batched by foreground colour and attributes) using one `FormattedText`
per run. Monospace cell metrics are measured from the chosen font so the grid stays
aligned. The cursor (block/beam/underline, blinking, hollow when unfocused) and a
scrollback indicator are drawn on top. Only the active tab's view is visible; inactive
views are collapsed so they do no rendering work.

Themes are resolved once by `ThemeResources` into frozen WPF brushes, including the
ANSI-16 palette and the xterm 256-colour cube; `TermColor` values (default / indexed /
RGB) map onto them at draw time.

## Terminal engine

`TerminalEmulator` is an explicit state-machine parser (ground / escape / CSI / OSC /
DCS-ignore) fed already-UTF8-decoded characters. Because parser state persists between
calls, a control sequence split across several reads is handled correctly. It drives
`TerminalBuffer`, which holds the active grid, a bounded scrollback, the cursor, scroll
region, insert/origin/autowrap modes and an alternate screen. Autowrap is deferred
(xterm-style): writing the last column latches a pending wrap resolved by the next
printable character.

Implemented sequences include cursor movement (CUU/CUD/CUF/CUB, CUP/HVP, CHA, VPA),
erase (ED/EL/ECH), insert/delete (ICH/DCH/IL/DL), scroll (SU/SD), scroll region
(DECSTBM), SGR (bold/faint/italic/underline/blink/inverse/hidden/strike; 16/256/true
colour), DEC private modes (cursor visibility, autowrap, origin, alternate screen,
bracketed paste), device status/attributes reports, and OSC 0/2 window titles. Mouse
tracking modes are parsed and consumed but not forwarded. This is a pragmatic subset,
not a complete xterm; see [`TESTING.md`](TESTING.md) and [`COMPATIBILITY.md`](COMPATIBILITY.md).

## Backend abstraction

`ITerminalBackend` abstracts the pseudoconsole host. `ConPtyBackend` is the sole
implementation. It:

1. Creates two anonymous pipes (input and output).
2. Calls `CreatePseudoConsole` with the input read end and output write end.
3. Builds a `STARTUPINFOEX` with a `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` attribute and
   calls `CreateProcess` for the shell.
4. Closes the pseudoconsole-side pipe ends (after `CreateProcess`, matching Microsoft's
   EchoCon ordering) and wraps our ends in `FileStream`s.
5. Reads output on a dedicated thread; waits for process exit on another; writes input
   under a lock.

Handles are tracked as raw `IntPtr` values and closed exactly once. `Dispose` closes
the pseudoconsole (which signals the shell to exit and unblocks the reader), disposes
the streams, and terminates the child after a short grace period so no orphaned shell
is left behind.

> **Host-context note:** a console-subsystem host with redirected standard handles can
> cause a child shell to see EOF on input and exit immediately. The app is a GUI
> (`winexe`) with no console, which avoids this; the integration tests are therefore
> also built as `winexe`. See [`TESTING.md`](TESTING.md).

## Session lifecycle

`TerminalSession` owns a backend, a buffer and an emulator and moves through explicit
states: `Created → Starting → Running → Stopping → Exited / Failed → Disposed`. Output
bytes are UTF-8 decoded on the read thread with a stateful `Decoder` (so multibyte
characters split across reads are preserved), then marshalled to the UI thread, where
the emulator and buffer are only ever touched by a single thread. Posts are coalesced
so heavy output cannot flood the dispatcher. Terminal replies (DSR/DA) are written back
to the shell.

## Configuration

Settings are a POCO serialised explicitly (no reflection) to JSON via a small built-in
`Json` reader/writer, stored at `%APPDATA%\Nixue\settings.json`. Loading never
throws: a missing file yields defaults, and a corrupt file is backed up and replaced.
`Validate` clamps every value, and missing keys fall back to defaults so an older file
still loads (schema-version aware).

## Logging

`Log` is a tiny, thread-safe, file-based logger that never throws, is bounded (1 MB with
a single rotation), and deliberately never records command contents, clipboard data or
environment secrets. It is off by default.

## Security model

Nixue CMD executes commands with the current user's permissions; it is not a
sandbox. It launches shell executables resolved from well-known system locations (never
an arbitrary `cmd.exe` from the working directory), passes user input through the
pseudoconsole rather than concatenating it into launch arguments, requests only
`asInvoker` privileges (no elevation), and never modifies global shell profiles or
system environment variables. The minimal prompt is applied to the app's own session
only (`cmd /K prompt …`, or a per-session PowerShell `prompt` function). There is no
networking and no telemetry.
