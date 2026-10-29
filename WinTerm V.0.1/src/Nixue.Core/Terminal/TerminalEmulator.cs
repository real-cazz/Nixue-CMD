using System;
using System.Collections.Generic;
using System.Text;

namespace Nixue.Core.Terminal
{
    /// <summary>
    /// A pragmatic VT100/VT220/xterm-subset terminal emulator. It is fed a stream of
    /// already-UTF8-decoded characters and drives a <see cref="TerminalBuffer"/>.
    ///
    /// The parser is an explicit state machine, so a control sequence that is split
    /// across several <see cref="Feed(string)"/> calls is handled correctly: the
    /// pending state survives between calls. Documented coverage lives in
    /// docs/ARCHITECTURE.md; this is not a complete xterm.
    /// </summary>
    public sealed class TerminalEmulator
    {
        private enum State { Ground, Escape, EscIntermediate, Csi, Osc, DcsIgnore }

        private readonly TerminalBuffer _buffer;
        private State _state = State.Ground;

        private readonly List<int> _params = new List<int>();
        private int _curParam = -1;           // -1 means "no digits yet"
        private char _csiPrefix = '\0';       // e.g. '?' for private modes
        private char _escIntermediate = '\0'; // e.g. '(' for charset
        private readonly StringBuilder _osc = new StringBuilder();
        private bool _oscEscPending;          // saw ESC inside OSC (maybe ST)

        private CellFlags _flags = CellFlags.None;

        public int TabWidth = 8;
        public bool BracketedPaste;
        public bool ApplicationCursorKeys;

        /// <summary>Title requested by the program via OSC 0/2.</summary>
        public event Action<string> TitleChanged;
        /// <summary>The program rang the bell (BEL).</summary>
        public event Action BellRang;
        /// <summary>A reply the terminal must send back to the shell (DSR, DA, ...).</summary>
        public event Action<string> ResponseReady;

        public TerminalEmulator(TerminalBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            _buffer = buffer;
        }

        public TerminalBuffer Buffer { get { return _buffer; } }
        public CellFlags CurrentFlags { get { return _flags; } }

        public void Feed(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < text.Length; i++)
            {
                FeedChar(text[i]);
            }
        }

        private void FeedChar(char ch)
        {
            switch (_state)
            {
                case State.Ground: Ground(ch); break;
                case State.Escape: Escape(ch); break;
                case State.EscIntermediate: EscIntermediate(ch); break;
                case State.Csi: Csi(ch); break;
                case State.Osc: Osc(ch); break;
                case State.DcsIgnore: DcsIgnore(ch); break;
            }
        }

        // ---- Ground ---------------------------------------------------------

        private void Ground(char ch)
        {
            if (ch == '\x1b') { _state = State.Escape; return; }
            if (ch < 0x20 || ch == 0x7f) { ExecuteC0(ch); return; }
            _lastPrintable = ch;
            _buffer.Print(ch, _flags);
        }

        private void ExecuteC0(char ch)
        {
            switch (ch)
            {
                case '\x07': if (BellRang != null) BellRang(); break; // BEL
                case '\x08': _buffer.Backspace(); break;              // BS
                case '\x09': _buffer.Tab(TabWidth); break;            // HT
                case '\x0a':                                          // LF
                case '\x0b':                                          // VT
                case '\x0c': _buffer.LineFeed(); break;               // FF
                case '\x0d': _buffer.CarriageReturn(); break;         // CR
                // SO/SI charset shifting and others are ignored.
            }
        }

        // ---- Escape ---------------------------------------------------------

        private void Escape(char ch)
        {
            if (ch == '[') { BeginCsi(); _state = State.Csi; return; }
            if (ch == ']') { _osc.Length = 0; _oscEscPending = false; _state = State.Osc; return; }
            if (ch == 'P' || ch == 'X' || ch == '^' || ch == '_')
            {
                // DCS / SOS / PM / APC: consume until ST.
                _state = State.DcsIgnore; _oscEscPending = false; return;
            }
            if (ch == '(' || ch == ')' || ch == '*' || ch == '+' || ch == '%' || ch == '#')
            {
                _escIntermediate = ch; _state = State.EscIntermediate; return;
            }

            switch (ch)
            {
                case 'D': _buffer.LineFeed(); break;         // IND
                case 'M': _buffer.ReverseLineFeed(); break;  // RI
                case 'E': _buffer.CarriageReturn(); _buffer.LineFeed(); break; // NEL
                case '7': SaveCursor(); break;
                case '8': RestoreCursor(); break;
                case '=': ApplicationCursorKeys = true; break;  // keypad application
                case '>': ApplicationCursorKeys = false; break; // keypad numeric
                case 'c': FullReset(); break;                // RIS
            }
            _state = State.Ground;
        }

        private void EscIntermediate(char ch)
        {
            // Charset designations such as ESC ( B. We accept and ignore the final byte.
            _escIntermediate = '\0';
            _state = State.Ground;
        }

        // ---- CSI ------------------------------------------------------------

        private void BeginCsi()
        {
            _params.Clear();
            _curParam = -1;
            _csiPrefix = '\0';
        }

        private void Csi(char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                if (_curParam < 0) _curParam = 0;
                long v = (long)_curParam * 10 + (ch - '0');
                if (v > 65535) { v = 65535; }
                _curParam = (int)v;
                return;
            }
            if (ch == ';')
            {
                _params.Add(_curParam < 0 ? 0 : _curParam);
                _curParam = -1;
                return;
            }
            if (ch == '?' || ch == '<' || ch == '=' || ch == '>')
            {
                _csiPrefix = ch;
                return;
            }
            if (ch >= 0x20 && ch <= 0x2f)
            {
                // Intermediate bytes (e.g. ' ' for cursor style). Remember the last.
                _escIntermediate = ch;
                return;
            }
            // Final byte.
            _params.Add(_curParam < 0 ? -1 : _curParam);
            DispatchCsi(ch);
            _escIntermediate = '\0';
            _state = State.Ground;
        }

        private int P(int index, int def)
        {
            if (index < 0 || index >= _params.Count) return def;
            int v = _params[index];
            return v < 0 ? def : v;
        }

        private void DispatchCsi(char final)
        {
            switch (final)
            {
                case 'A': _buffer.MoveCursor(0, -Math.Max(1, P(0, 1))); break;
                case 'B': _buffer.MoveCursor(0, Math.Max(1, P(0, 1))); break;
                case 'C': _buffer.MoveCursor(Math.Max(1, P(0, 1)), 0); break;
                case 'D': _buffer.MoveCursor(-Math.Max(1, P(0, 1)), 0); break;
                case 'E': _buffer.CarriageReturn(); _buffer.MoveCursor(0, Math.Max(1, P(0, 1))); break;
                case 'F': _buffer.CarriageReturn(); _buffer.MoveCursor(0, -Math.Max(1, P(0, 1))); break;
                case 'G': _buffer.SetCursorAbsolute(P(0, 1) - 1, _buffer.CursorY); break; // CHA
                case 'H':
                case 'f': _buffer.SetCursor(P(1, 1) - 1, P(0, 1) - 1); break;             // CUP
                case 'd': _buffer.SetCursorAbsolute(_buffer.CursorX, P(0, 1) - 1); break; // VPA
                case 'J': _buffer.EraseInDisplay(P(0, 0)); break;
                case 'K': _buffer.EraseInLine(P(0, 0)); break;
                case 'L': _buffer.InsertLines(Math.Max(1, P(0, 1))); break;
                case 'M': _buffer.DeleteLines(Math.Max(1, P(0, 1))); break;
                case '@': _buffer.InsertChars(Math.Max(1, P(0, 1))); break;
                case 'P': _buffer.DeleteChars(Math.Max(1, P(0, 1))); break;
                case 'X': _buffer.EraseChars(Math.Max(1, P(0, 1))); break;
                case 'S': _buffer.ScrollUp(Math.Max(1, P(0, 1))); break;
                case 'T': _buffer.ScrollDown(Math.Max(1, P(0, 1))); break;
                case 'Z': CursorBackTab(Math.Max(1, P(0, 1))); break;
                case 'b': RepeatLast(Math.Max(1, P(0, 1))); break;
                case 'm': ApplySgr(); break;
                case 'r': SetScrollRegionCsi(); break;
                case 's': SaveCursor(); break;
                case 'u': RestoreCursor(); break;
                case 'h': SetModes(true); break;
                case 'l': SetModes(false); break;
                case 'n': DeviceStatusReport(); break;
                case 'c': DeviceAttributes(); break;
                // 'q' with ' ' intermediate = cursor style (DECSCUSR); styling handled
                // in the UI; nothing to do in the model.
            }
        }

        private char _lastPrintable = '\0';

        private void RepeatLast(int n)
        {
            if (_lastPrintable == '\0') return;
            for (int i = 0; i < n; i++) _buffer.Print(_lastPrintable, _flags);
        }

        private void CursorBackTab(int n)
        {
            for (int i = 0; i < n; i++)
            {
                int x = _buffer.CursorX - 1;
                if (x < 0) { x = 0; break; }
                x = (x / TabWidth) * TabWidth;
                _buffer.SetCursorAbsolute(x, _buffer.CursorY);
            }
        }

        private void SetScrollRegionCsi()
        {
            int top = P(0, 1) - 1;
            int bottom = P(1, _buffer.Rows) - 1;
            _buffer.SetScrollRegion(top, bottom);
        }

        private void SetModes(bool enable)
        {
            if (_csiPrefix == '?')
            {
                for (int i = 0; i < _params.Count; i++)
                {
                    int m = _params[i];
                    switch (m)
                    {
                        case 1: ApplicationCursorKeys = enable; break;   // DECCKM
                        case 6: _buffer.OriginMode = enable; _buffer.SetCursor(0, 0); break; // DECOM
                        case 7: _buffer.AutoWrap = enable; break;        // DECAWM
                        case 25: _buffer.CursorVisible = enable; break;  // DECTCEM
                        case 1049:
                        case 1047:
                        case 47:
                            if (enable) _buffer.EnterAltScreen(m != 47);
                            else _buffer.LeaveAltScreen();
                            break;
                        case 2004: BracketedPaste = enable; break;
                        // Mouse modes (1000/1002/1003/1006) are consumed but not acted on.
                    }
                }
            }
            else
            {
                for (int i = 0; i < _params.Count; i++)
                {
                    int m = _params[i];
                    if (m == 4) _buffer.InsertMode = enable; // IRM
                }
            }
        }

        private void DeviceStatusReport()
        {
            int code = P(0, 0);
            if (ResponseReady == null) return;
            if (code == 5)
            {
                ResponseReady("\x1b[0n"); // OK
            }
            else if (code == 6)
            {
                // Cursor Position Report (1-based).
                int row = _buffer.CursorY + 1;
                int col = _buffer.CursorX + 1;
                ResponseReady("\x1b[" + row + ";" + col + "R");
            }
        }

        private void DeviceAttributes()
        {
            if (ResponseReady != null)
            {
                // Claim to be a VT102-ish terminal.
                ResponseReady("\x1b[?6c");
            }
        }

        // ---- SGR ------------------------------------------------------------

        private void ApplySgr()
        {
            if (_params.Count == 0 || (_params.Count == 1 && _params[0] <= 0))
            {
                ResetSgr();
                return;
            }

            for (int i = 0; i < _params.Count; i++)
            {
                int code = _params[i];
                if (code < 0) code = 0;
                switch (code)
                {
                    case 0: ResetSgr(); break;
                    case 1: _flags |= CellFlags.Bold; break;
                    case 2: _flags |= CellFlags.Faint; break;
                    case 3: _flags |= CellFlags.Italic; break;
                    case 4: _flags |= CellFlags.Underline; break;
                    case 5:
                    case 6: _flags |= CellFlags.Blink; break;
                    case 7: _flags |= CellFlags.Inverse; break;
                    case 8: _flags |= CellFlags.Hidden; break;
                    case 9: _flags |= CellFlags.Strike; break;
                    case 21:
                    case 22: _flags &= ~(CellFlags.Bold | CellFlags.Faint); break;
                    case 23: _flags &= ~CellFlags.Italic; break;
                    case 24: _flags &= ~CellFlags.Underline; break;
                    case 25: _flags &= ~CellFlags.Blink; break;
                    case 27: _flags &= ~CellFlags.Inverse; break;
                    case 28: _flags &= ~CellFlags.Hidden; break;
                    case 29: _flags &= ~CellFlags.Strike; break;
                    case 39: _buffer.CurrentFg = TermColor.Default; break;
                    case 49: _buffer.CurrentBg = TermColor.Default; break;
                    case 38: i = ExtendedColor(i, true); break;
                    case 48: i = ExtendedColor(i, false); break;
                    default:
                        if (code >= 30 && code <= 37) _buffer.CurrentFg = TermColor.FromIndex(code - 30);
                        else if (code >= 40 && code <= 47) _buffer.CurrentBg = TermColor.FromIndex(code - 40);
                        else if (code >= 90 && code <= 97) _buffer.CurrentFg = TermColor.FromIndex(code - 90 + 8);
                        else if (code >= 100 && code <= 107) _buffer.CurrentBg = TermColor.FromIndex(code - 100 + 8);
                        break;
                }
            }
        }

        private int ExtendedColor(int i, bool foreground)
        {
            // i points at 38/48. Next param selects 5 (indexed) or 2 (rgb).
            int mode = (i + 1 < _params.Count) ? _params[i + 1] : -1;
            if (mode == 5)
            {
                int idx = (i + 2 < _params.Count) ? _params[i + 2] : 0;
                TermColor c = TermColor.FromIndex(idx);
                if (foreground) _buffer.CurrentFg = c; else _buffer.CurrentBg = c;
                return i + 2;
            }
            if (mode == 2)
            {
                int r = (i + 2 < _params.Count) ? _params[i + 2] : 0;
                int g = (i + 3 < _params.Count) ? _params[i + 3] : 0;
                int b = (i + 4 < _params.Count) ? _params[i + 4] : 0;
                TermColor c = TermColor.FromRgb(ClampByte(r), ClampByte(g), ClampByte(b));
                if (foreground) _buffer.CurrentFg = c; else _buffer.CurrentBg = c;
                return i + 4;
            }
            return i;
        }

        private static byte ClampByte(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        private void ResetSgr()
        {
            _flags = CellFlags.None;
            _buffer.CurrentFg = TermColor.Default;
            _buffer.CurrentBg = TermColor.Default;
        }

        // ---- OSC ------------------------------------------------------------

        private void Osc(char ch)
        {
            if (_oscEscPending)
            {
                _oscEscPending = false;
                if (ch == '\\') { FinishOsc(); return; }       // ST
                // Lone ESC inside OSC: treat as terminator too.
                FinishOsc();
                FeedChar(ch);
                return;
            }
            if (ch == '\x07') { FinishOsc(); return; }          // BEL terminator
            if (ch == '\x1b') { _oscEscPending = true; return; } // maybe ST
            _osc.Append(ch);
        }

        private void FinishOsc()
        {
            string s = _osc.ToString();
            _osc.Length = 0;
            _state = State.Ground;

            int semi = s.IndexOf(';');
            if (semi >= 0)
            {
                string code = s.Substring(0, semi);
                string arg = s.Substring(semi + 1);
                if ((code == "0" || code == "2") && TitleChanged != null)
                {
                    TitleChanged(arg);
                }
            }
        }

        private void DcsIgnore(char ch)
        {
            if (_oscEscPending)
            {
                _oscEscPending = false;
                if (ch == '\\') { _state = State.Ground; return; }
                _state = State.Ground;
                FeedChar(ch);
                return;
            }
            if (ch == '\x1b') { _oscEscPending = true; return; }
            if (ch == '\x07') { _state = State.Ground; return; }
            // otherwise swallow
        }

        // ---- Cursor save / restore / reset ---------------------------------

        private int _savedX, _savedY;
        private CellFlags _savedFlags;
        private TermColor _savedFg, _savedBg;

        private void SaveCursor()
        {
            _savedX = _buffer.CursorX;
            _savedY = _buffer.CursorY;
            _savedFlags = _flags;
            _savedFg = _buffer.CurrentFg;
            _savedBg = _buffer.CurrentBg;
        }

        private void RestoreCursor()
        {
            _buffer.SetCursorAbsolute(_savedX, _savedY);
            _flags = _savedFlags;
            _buffer.CurrentFg = _savedFg;
            _buffer.CurrentBg = _savedBg;
        }

        private void FullReset()
        {
            _flags = CellFlags.None;
            BracketedPaste = false;
            ApplicationCursorKeys = false;
            _buffer.HardReset();
        }
    }
}
