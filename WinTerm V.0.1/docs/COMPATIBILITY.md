# Compatibility

## Minimum supported Windows version

**Windows 10 version 1809 (build 17763) or newer, including Windows 11.**

The terminal backend uses the Windows pseudoconsole (ConPTY) API
(`CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`), which was
introduced in Windows 10 build 17763. On older builds the app detects the missing
capability at startup, shows an actionable message, and exits with a non-zero code
rather than failing obscurely.

## Runtime dependencies

- **.NET Framework 4.x** (desktop, with WPF). Present in-box on Windows 10 and 11.
- No other runtime, redistributable, or installed component is required. The app is
  self-contained apart from the OS runtime and works offline.

## Verified vs expected

| Item | Status |
| --- | --- |
| Windows 11, build 26300 (x64) | **Verified** — build, unit tests, integration tests against real cmd/PowerShell, and interactive GUI use all confirmed. |
| Windows 10 1809–22H2 | **Expected to work** (ConPTY present; same APIs) — not individually tested here. |
| Windows 10 pre-1809, Windows 8.1, 8, 7, XP | **Not supported** — no ConPTY. The app exits with a clear message. |
| ARM64 Windows | **Expected to run** under x64 emulation; a native ARM64 build is not produced. |

Only the Windows 11 build above was available during development; everything else in
this table is a reasoned expectation, not a tested result.

## ConPTY requirement and the GUI-host note

A subtle but important detail: a **console-subsystem** host process that has its
standard input redirected can cause a spawned shell to observe EOF on input and exit
immediately. Nixue CMD is a **GUI** application (`winexe`, no console), which does
not have this problem — interactive shells stay alive and receive input normally. This
was verified both via the GUI and via the integration tests (which are built as
`winexe` for the same reason). See [`TESTING.md`](TESTING.md).

## Shell availability

- **CMD** — resolved from the system directory (`%SystemRoot%\System32\cmd.exe`);
  effectively always present.
- **Windows PowerShell** — resolved from
  `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`; present on all
  supported versions.
- **PowerShell 7 (`pwsh.exe`)** — optional. Discovered under `Program Files\PowerShell`,
  the per-user WindowsApps location, and `PATH`. If absent, the shell menu shows it as
  unavailable with a reason; it is never silently replaced by another shell.

## Known limitations

- **No reflow on resize.** When the window is resized, lines are clipped/extended to the
  new width rather than re-wrapped. Scrollback width is clamped.
- **Mouse reporting not forwarded.** Mouse-tracking escape sequences (1000/1002/1003/
  1006) are parsed and consumed so they do not corrupt output, but mouse events are not
  sent to the shell. Mouse-driven TUIs will not receive clicks.
- **Bold / italic depend on the font.** Faux styling is not applied; if the chosen
  monospaced font lacks a bold or italic face, those attributes render in the regular
  face. Underline and strike-through are drawn by the renderer.
- **`blink` attribute is rendered statically** (text does not flash) to avoid visual
  noise; the cursor blink is independent and configurable.
- **Single process architecture, multiple windows supported.** `Ctrl+Shift+N` opens an
  additional window in the same process; closing one window does not affect sessions in
  another.
- **DEC line-drawing / national character sets** are not specially mapped; UTF-8 box
  drawing characters render directly.
- **Not every xterm sequence is implemented** — see the implemented set in
  [`ARCHITECTURE.md`](ARCHITECTURE.md). DCS/APC/PM/SOS strings are consumed and ignored.
