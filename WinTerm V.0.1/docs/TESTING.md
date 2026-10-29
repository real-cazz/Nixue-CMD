# Testing

The environment has no test SDK (no `dotnet`, no VSTest), so the suites are small,
self-contained runners compiled with the in-box compiler and executed directly. Both
run fully offline. `scripts\test.ps1` builds and runs both and returns a non-zero exit
code if anything fails.

## Unit tests — `tests/Nixue.Tests`

Deterministic, no shells, no UI. Runner: `Nixue.Tests.exe` (console), references
`Nixue.Core.dll`.

**Verified result (Windows 11, build 26300): 36 / 36 passed.**

Coverage:

- **JSON**: round-trip, nested structures and escapes, rejection of malformed input.
- **Settings**: defaults; round-trip; invalid-value clamping; missing keys fall back to
  defaults; atomic save/load; corrupt-file recovery (backup + defaults).
- **Themes**: four built-ins, lookup by name, JSON fallback/override.
- **Shell discovery**: CMD is found and validated; CMD `/K prompt` command line maps
  `>`→`$G`; PowerShell command line uses `-NoProfile` and defines a session prompt;
  prompt-template mapping and PowerShell prompt sanitisation.
- **VT parser / emulator**: printing, CR/LF, backspace, CUP cursor positioning, SGR
  indexed and true colour, erase-in-line, **control sequence split across feeds**,
  deferred autowrap, alternate screen enter/leave, scrollback growth, DSR cursor-position
  reply, OSC title, tab stops.
- **Buffer**: resize grow/shrink with cursor clamping, insert/delete chars, insert/
  delete lines, scroll region set/reset, scrollback-limit enforcement.
- **UTF-8**: a multibyte character split across byte reads is reassembled by the stateful
  decoder (the mechanism the session uses).
- **Compatibility**: detection produces a usable report.

## Integration tests — `tests/Nixue.IntegrationTests`

Spawns **real shells** through the ConPTY backend and asserts on what they produce.
Runner: `Nixue.IntegrationTests.exe`, built as a **winexe** (no console
subsystem) so the spawned shells inherit a real pseudoconsole — matching the GUI app's
host context — and writes its report to a file the test script prints.

**Verified result (Windows 11, build 26300): 9 / 9 passed.**

| Test | What it proves |
| --- | --- |
| `Cmd_StartsAndEchoes` | CMD starts and `echo` output appears in the buffer. |
| `Cmd_WorkingDirectoryIndependentSessions` | Two CMD tabs keep independent working directories. |
| `Cmd_ExitDetected` | `exit` is detected and the session transitions to `Exited`. |
| `Cmd_UnicodeOutput` | UTF-8 (code page 65001) output is handled. |
| `Cmd_LargeOutput` | 300 lines complete; scrollback stays within its limit. |
| `PowerShell_StartsAndEvaluates` | Windows PowerShell evaluates an expression (`2+5`). |
| `Pwsh7_HandledWhenMissing` | PowerShell 7 is either validated or reported unavailable with a reason. |
| `Resize_DoesNotThrow` | `ResizePseudoConsole` applies new dimensions. |
| `Dispose_LeavesNoProcess` | The session disposes cleanly to the `Disposed` state. |

Each test has a timeout and disposes its session, so no shell process is left behind.

## Manual / GUI verification (performed during development)

Captured live from a running build on Windows 11:

- Application launches, loads settings, starts a CMD ConPTY session (80×24) and opens a
  CMD tab without error.
- The window renders the intended dark, sharp-cornered chrome: `NIXUE CMD` title
  bar with the `>_` mark, compact tab strip (active tab with accent border and a green
  running dot), the minimal `>` prompt with a blinking block cursor, and the status bar
  (`CMD | 132x34 | running | 14pt | Nixue Dark | ConPTY`).
- Typing `echo Hello from NIXUE CMD` and `ver` through the keyboard produced the
  expected echoed commands and output, confirming the full keyboard → ConPTY → parser →
  renderer path.

A screenshot is in [`screenshot.png`](screenshot.png).

## Not automated here

The following were validated manually or are left for manual testing, as automated UI
testing infrastructure is not available in this environment:

- Tab switching/closing via the mouse and shortcuts, drag-free tab strip behaviour.
- Settings window round-trip for every control, live theme/font-size changes.
- Fullscreen toggle, maximise/restore, multi-monitor placement and DPI scaling.
- Mouse text selection, copy-on-select, and the multiline-paste warning dialog.
- Scrollback navigation with the mouse wheel and `Shift`+navigation keys.

## Reproducing

```powershell
powershell -File scripts\test.ps1
```
