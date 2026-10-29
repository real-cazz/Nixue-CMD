using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nixue.Core.Config;
using Nixue.Core.Diagnostics;
using Nixue.Core.Terminal;
using Nixue.App.Sessions;

namespace Nixue.App.UI
{
    /// <summary>
    /// The terminal viewport. Renders the session's grid as character cells, draws the
    /// cursor and selection, handles keyboard/mouse input and the clipboard, and keeps
    /// the pseudoconsole sized to the control. Drawing uses one <see cref="FormattedText"/>
    /// per style-run (batched by colour/attributes) so glyph fallback and metrics stay
    /// correct while the visible grid is small.
    /// </summary>
    public sealed class TerminalView : FrameworkElement
    {
        private TerminalSession _session;
        private ThemeResources _theme;
        private AppSettings _settings;

        private Typeface _regular, _bold, _italic, _boldItalic;
        private double _emSize = 14;
        private double _cellWidth = 8;
        private double _cellHeight = 16;
        private double _baseline = 12;
        private double _pixelsPerDip = 1.0;
        private const double PadLeft = 6;
        private const double PadTop = 4;

        private readonly DispatcherTimer _animTimer;  // drives smooth cursor fade + trail
        private bool _hasFocus;
        private int _animBaseTick;

        // Cursor movement trail ("misty shadowy" glow left behind while typing).
        private struct TrailCell { public int Col; public int Line; public int Tick; }
        private readonly List<TrailCell> _trail = new List<TrailCell>();
        private int _prevCursorCol = -1;
        private int _prevCursorLine = -1;
        private const int TrailMs = 650;
        private const int TrailMaxAlpha = 90;

        // Scrollback view state.
        private bool _followBottom = true;
        private int _topLine;

        // Selection (logical coordinates: absolute line index + column).
        private bool _selecting;
        private bool _hasSelection;
        private int _selStartLine, _selStartCol, _selEndLine, _selEndCol;
        private DateTime _lastClick = DateTime.MinValue;
        private int _clickCount;

        public event Action<string> TitleChanged;
        public event Action SessionExited;

        public TerminalView()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            _animBaseTick = Environment.TickCount;
            _animTimer = new DispatcherTimer();
            _animTimer.Interval = TimeSpan.FromMilliseconds(40); // ~25 fps, smooth fade
            _animTimer.Tick += delegate { InvalidateVisual(); };

            Loaded += delegate { Focus(); };
        }

        public TerminalSession Session { get { return _session; } }

        public void Attach(TerminalSession session, ThemeResources theme, AppSettings settings)
        {
            _session = session;
            _theme = theme;
            _settings = settings;
            RebuildFonts();
            ConfigureBlink();

            session.Updated += OnSessionUpdated;
            session.TitleChanged += OnSessionTitle;
            session.Exited += OnSessionExited;
            session.Bell += OnBell;
            InvalidateVisual();
        }

        public void ApplyTheme(ThemeResources theme)
        {
            _theme = theme;
            InvalidateVisual();
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            RebuildFonts();
            ConfigureBlink();
            if (_session != null) _session.SetScrollbackLimit(settings.ScrollbackLimit);
            SyncSize();
            InvalidateVisual();
        }

        // ---- Fonts & metrics -----------------------------------------------

        private void RebuildFonts()
        {
            FontFamily ff = LoadCustomFont();
            if (ff == null)
            {
                string family = PickFontFamily(_settings != null ? _settings.TerminalFont : "Consolas");
                ff = new FontFamily(family);
            }
            _regular = new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _bold = new Typeface(ff, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            _italic = new Typeface(ff, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
            _boldItalic = new Typeface(ff, FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);

            _emSize = _settings != null ? _settings.TerminalFontSize : 14.0;
            try { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { _pixelsPerDip = 1.0; }

            // Measure a monospace cell from a run of identical glyphs.
            FormattedText probe = NewText("MMMMMMMMMM", _regular, Brushes.White);
            _cellWidth = Math.Max(1.0, probe.WidthIncludingTrailingWhitespace / 10.0);
            double lineSpacing = _settings != null ? _settings.LineSpacing : 1.0;
            FormattedText lineProbe = NewText("Mg", _regular, Brushes.White);
            _cellHeight = Math.Max(1.0, Math.Ceiling(lineProbe.Height * lineSpacing));
            _baseline = lineProbe.Baseline;
        }

        /// <summary>
        /// Loads a user-uploaded .ttf as the terminal font, if one is configured and
        /// present. Returns null to fall back to the named-font preference list.
        /// </summary>
        private FontFamily LoadCustomFont()
        {
            if (_settings == null) return null;
            string file = _settings.TerminalFontFile;
            if (string.IsNullOrEmpty(file)) return null;
            try
            {
                if (!File.Exists(file)) return null;
                ICollection<FontFamily> fams = Fonts.GetFontFamilies(file);
                foreach (FontFamily f in fams) return f; // first family in the file
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to load custom font '" + file + "': " + ex.Message);
            }
            return null;
        }

        private static string PickFontFamily(string preferenceList)
        {
            if (string.IsNullOrEmpty(preferenceList)) return "Consolas";
            string[] names = preferenceList.Split(',');
            // Enumerate installed families once.
            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (FontFamily f in Fonts.SystemFontFamilies)
                {
                    foreach (KeyValuePair<System.Windows.Markup.XmlLanguage, string> kv in f.FamilyNames)
                        installed.Add(kv.Value);
                }
            }
            catch { }
            foreach (string raw in names)
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                if (installed.Count == 0 || installed.Contains(n)) return n;
            }
            // Fallbacks that ship with Windows.
            if (installed.Contains("Consolas")) return "Consolas";
            if (installed.Contains("Lucida Console")) return "Lucida Console";
            return "Courier New";
        }

        private FormattedText NewText(string s, Typeface tf, Brush brush)
        {
            return new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                     tf, _emSize, brush, _pixelsPerDip);
        }

        // ---- Sizing --------------------------------------------------------

        public int ColsForSize { get { return Math.Max(1, (int)((ActualWidth - PadLeft * 2) / _cellWidth)); } }
        public int RowsForSize { get { return Math.Max(1, (int)((ActualHeight - PadTop * 2) / _cellHeight)); } }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            SyncSize();
            InvalidateVisual();
        }

        private void SyncSize()
        {
            if (_session == null) return;
            int cols = ColsForSize;
            int rows = RowsForSize;
            if (cols != _session.Cols || rows != _session.Rows)
            {
                _session.Resize(cols, rows);
            }
        }

        // ---- Session events ------------------------------------------------

        private void OnSessionUpdated()
        {
            InvalidateVisual();
        }

        private void OnSessionTitle(string title)
        {
            Action<string> h = TitleChanged;
            if (h != null) h(title);
        }

        private void OnSessionExited(int code)
        {
            Action h = SessionExited;
            if (h != null) h();
            InvalidateVisual();
        }

        private void OnBell()
        {
            if (_settings != null && _settings.BellEnabled)
            {
                try { System.Media.SystemSounds.Beep.Play(); } catch { }
            }
        }

        private void ConfigureBlink()
        {
            // The animation timer drives both the smooth cursor fade and the typing
            // trail, so it runs whenever the view is focused (regardless of the blink
            // setting - the trail still animates with blink off).
            if (_hasFocus) _animTimer.Start();
            else _animTimer.Stop();
        }

        /// <summary>Smoothly fading cursor opacity in [0.2, 1.0]; 1.0 when blink is off.</summary>
        private double CursorOpacity()
        {
            if (_settings == null || !_settings.CursorBlink) return 1.0;
            int rate = _settings.CursorBlinkRateMs;
            if (rate < 100) rate = 100;
            double period = rate * 2.0; // full fade-out-and-in cycle
            double t = ((Environment.TickCount - _animBaseTick) % period) / period;
            double wave = 0.5 + 0.5 * Math.Cos(2.0 * Math.PI * t); // 1..0..1 smoothly
            return 0.2 + 0.8 * wave;
        }

        // ---- Focus ---------------------------------------------------------

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            _hasFocus = true;
            _animTimer.Start();
            InvalidateVisual();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            _hasFocus = false;
            _animTimer.Stop();
            InvalidateVisual();
        }

        /// <summary>Called by the window when it is minimised/restored to pause animation.</summary>
        public void SetRenderingActive(bool active)
        {
            if (!active) _animTimer.Stop();
            else if (_hasFocus) _animTimer.Start();
        }

        // ---- Rendering -----------------------------------------------------

        protected override void OnRender(DrawingContext dc)
        {
            Brush bg = _theme != null ? _theme.TerminalBackgroundBrush : Brushes.Black;
            dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (_session == null || _theme == null) return;

            TerminalBuffer buf = _session.Buffer;
            int rows = RowsForSize;
            int cols = ColsForSize;
            int total = buf.TotalLines;

            int firstVisible;
            if (_followBottom) firstVisible = Math.Max(0, total - rows);
            else firstVisible = Math.Max(0, Math.Min(_topLine, Math.Max(0, total - rows)));

            TrackTrail(buf);

            // Pass 1: cell backgrounds and selection.
            for (int r = 0; r < rows; r++)
            {
                int lineIndex = firstVisible + r;
                if (lineIndex < 0 || lineIndex >= total) continue;
                TerminalCell[] line = buf.GetLogicalLine(lineIndex);
                double y = PadTop + r * _cellHeight;
                DrawLineBackgrounds(dc, line, cols, y, lineIndex);
            }

            // Pass 2: the misty cursor trail, behind the text.
            DrawTrail(dc, firstVisible, rows, cols);

            // Pass 3: glyphs.
            for (int r = 0; r < rows; r++)
            {
                int lineIndex = firstVisible + r;
                if (lineIndex < 0 || lineIndex >= total) continue;
                TerminalCell[] line = buf.GetLogicalLine(lineIndex);
                double y = PadTop + r * _cellHeight;
                DrawLineText(dc, line, cols, y);
            }

            DrawCursor(dc, buf, firstVisible, rows, cols);
            DrawScrollIndicator(dc, firstVisible, rows, total);
        }

        private void TrackTrail(TerminalBuffer buf)
        {
            int now = Environment.TickCount;
            // Expire old trail cells.
            for (int i = _trail.Count - 1; i >= 0; i--)
                if (now - _trail[i].Tick > TrailMs) _trail.RemoveAt(i);

            int col = buf.CursorX;
            int line = buf.ScrollbackCount + buf.CursorY;

            // Leave a trail only on simple rightward typing on the same line.
            if (_prevCursorLine == line && col > _prevCursorCol && (col - _prevCursorCol) <= 8)
            {
                for (int c = _prevCursorCol; c < col; c++)
                {
                    TrailCell tc = new TrailCell();
                    tc.Col = c; tc.Line = line; tc.Tick = now;
                    _trail.Add(tc);
                }
            }
            _prevCursorCol = col;
            _prevCursorLine = line;
        }

        private void DrawTrail(DrawingContext dc, int firstVisible, int rows, int cols)
        {
            if (_trail.Count == 0) return;
            int now = Environment.TickCount;
            Color accent = _theme.Accent;
            for (int i = 0; i < _trail.Count; i++)
            {
                TrailCell tc = _trail[i];
                int row = tc.Line - firstVisible;
                if (row < 0 || row >= rows) continue;
                if (tc.Col < 0 || tc.Col >= cols) continue;
                double frac = 1.0 - (now - tc.Tick) / (double)TrailMs;
                if (frac <= 0) continue;
                byte a = (byte)(TrailMaxAlpha * frac * frac); // ease-out for a soft tail
                double x = PadLeft + tc.Col * _cellWidth;
                double y = PadTop + row * _cellHeight;
                // Vertical gradient gives the "misty" look.
                LinearGradientBrush g = new LinearGradientBrush(
                    Color.FromArgb(a, accent.R, accent.G, accent.B),
                    Color.FromArgb((byte)(a / 3), accent.R, accent.G, accent.B),
                    90.0);
                g.Freeze();
                dc.DrawRectangle(g, null, new Rect(x, y, _cellWidth, _cellHeight));
            }
        }

        private void DrawLineBackgrounds(DrawingContext dc, TerminalCell[] line, int cols, double y, int lineIndex)
        {
            int n = Math.Min(cols, line.Length);
            int c = 0;
            while (c < n)
            {
                Color bg = CellBg(line[c], c, lineIndex);
                bool sel = IsSelected(lineIndex, c);
                int start = c;
                c++;
                while (c < n && CellBg(line[c], c, lineIndex).Equals(bg) && IsSelected(lineIndex, c) == sel) c++;
                double x = PadLeft + start * _cellWidth;
                double w = (c - start) * _cellWidth;
                if (sel)
                {
                    dc.DrawRectangle(_theme.SelectionBrush, null, new Rect(x, y, w, _cellHeight));
                }
                else if (!bg.Equals(_theme.TerminalBackground))
                {
                    dc.DrawRectangle(_theme.BrushFor(bg), null, new Rect(x, y, w, _cellHeight));
                }
            }
        }

        private void DrawLineText(DrawingContext dc, TerminalCell[] line, int cols, double y)
        {
            int n = Math.Min(cols, line.Length);
            int c = 0;
            while (c < n)
            {
                TerminalCell cell = line[c];
                if (cell.Char == ' ' || cell.Char == '\0' ||
                    (cell.Flags & CellFlags.Hidden) != 0)
                {
                    c++;
                    continue;
                }
                CellFlags flags = cell.Flags;
                Color fg = CellFg(cell);
                int start = c;
                StringBuilder sb = new StringBuilder();
                sb.Append(cell.Char);
                c++;
                while (c < n)
                {
                    TerminalCell next = line[c];
                    if (next.Char == ' ' || next.Char == '\0') break;
                    if ((next.Flags & CellFlags.Hidden) != 0) break;
                    if (next.Flags != flags) break;
                    if (!CellFg(next).Equals(fg)) break;
                    sb.Append(next.Char);
                    c++;
                }
                double x = PadLeft + start * _cellWidth;
                DrawRun(dc, sb.ToString(), flags, fg, x, y);
            }
        }

        private void DrawRun(DrawingContext dc, string text, CellFlags flags, Color fg, double x, double y)
        {
            bool bold = (flags & CellFlags.Bold) != 0;
            bool italic = (flags & CellFlags.Italic) != 0;
            Typeface tf = bold ? (italic ? _boldItalic : _bold) : (italic ? _italic : _regular);

            if ((flags & CellFlags.Faint) != 0)
                fg = Color.FromArgb(160, fg.R, fg.G, fg.B);

            Brush brush = _theme.BrushFor(fg);
            FormattedText ft = NewText(text, tf, brush);
            dc.DrawText(ft, new Point(x, y));

            if ((flags & CellFlags.Underline) != 0)
            {
                double uy = y + _baseline + 1.5;
                dc.DrawRectangle(brush, null, new Rect(x, uy, text.Length * _cellWidth, 1.0));
            }
            if ((flags & CellFlags.Strike) != 0)
            {
                double sy = y + _cellHeight * 0.5;
                dc.DrawRectangle(brush, null, new Rect(x, sy, text.Length * _cellWidth, 1.0));
            }
        }

        private Color CellFg(TerminalCell cell)
        {
            bool inverse = (cell.Flags & CellFlags.Inverse) != 0;
            if (inverse) return _theme.ResolveColor(cell.Bg, true);
            return _theme.ResolveColor(cell.Fg, false);
        }

        private Color CellBg(TerminalCell cell, int col, int lineIndex)
        {
            bool inverse = (cell.Flags & CellFlags.Inverse) != 0;
            if (inverse) return _theme.ResolveColor(cell.Fg, false);
            return _theme.ResolveColor(cell.Bg, true);
        }

        private void DrawCursor(DrawingContext dc, TerminalBuffer buf, int firstVisible, int rows, int cols)
        {
            if (!buf.CursorVisible) return;
            if (_settings == null) return;
            if (_session != null && _session.State == SessionState.Exited) return;

            int cursorLine = buf.ScrollbackCount + buf.CursorY;
            int row = cursorLine - firstVisible;
            if (row < 0 || row >= rows) return;
            int colc = buf.CursorX;
            if (colc < 0 || colc >= cols) return;

            double x = PadLeft + colc * _cellWidth;
            double y = PadTop + row * _cellHeight;
            Color c = _theme.Cursor;

            if (!_hasFocus)
            {
                // Hollow block when not focused (steady, no fade).
                Pen pen = new Pen(_theme.CursorBrush, 1.0);
                dc.DrawRectangle(null, pen, new Rect(x + 0.5, y + 0.5, _cellWidth - 1, _cellHeight - 1));
                return;
            }

            double op = CursorOpacity();
            byte a = (byte)(op * 255);
            Brush cur = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));

            CursorStyle style = _settings.CursorStyle;
            if (style == CursorStyle.Beam)
            {
                dc.DrawRectangle(cur, null, new Rect(x, y, 2.0, _cellHeight));
            }
            else if (style == CursorStyle.Underline)
            {
                dc.DrawRectangle(cur, null, new Rect(x, y + _cellHeight - 2, _cellWidth, 2.0));
            }
            else
            {
                // Translucent block drawn over the glyph (already painted), so the
                // character shows softly through the fading cursor.
                dc.DrawRectangle(cur, null, new Rect(x, y, _cellWidth, _cellHeight));
            }
        }

        private void DrawScrollIndicator(DrawingContext dc, int firstVisible, int rows, int total)
        {
            if (_followBottom || total <= rows) return;
            double trackH = ActualHeight - PadTop * 2;
            double thumbH = Math.Max(24, trackH * rows / total);
            double maxTop = Math.Max(1, total - rows);
            double t = (double)firstVisible / maxTop;
            double thumbY = PadTop + t * (trackH - thumbH);
            Rect r = new Rect(ActualWidth - 4, thumbY, 3, thumbH);
            dc.DrawRectangle(_theme.AccentBrush, null, r);
        }

        // ---- Scrolling -----------------------------------------------------

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_session == null) return;
            int lines = 3 * Math.Sign(e.Delta);
            ScrollByLines(-lines);
            e.Handled = true;
        }

        private void ScrollByLines(int delta)
        {
            TerminalBuffer buf = _session.Buffer;
            int rows = RowsForSize;
            int total = buf.TotalLines;
            int maxTop = Math.Max(0, total - rows);

            int current = _followBottom ? maxTop : _topLine;
            int next = current + delta;
            if (next < 0) next = 0;
            if (next >= maxTop) { _followBottom = true; _topLine = maxTop; }
            else { _followBottom = false; _topLine = next; }
            InvalidateVisual();
        }

        public void ScrollToBottom()
        {
            _followBottom = true;
            InvalidateVisual();
        }

        // ---- Mouse selection -----------------------------------------------

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            if (_session == null) return;

            DateTime now = DateTime.Now;
            if ((now - _lastClick).TotalMilliseconds < 400) _clickCount++;
            else _clickCount = 1;
            _lastClick = now;

            Point p = e.GetPosition(this);
            int line, col;
            PointToCell(p, out line, out col);

            if (_clickCount == 2) { SelectWord(line, col); }
            else if (_clickCount >= 3) { SelectLine(line); }
            else
            {
                _selecting = true;
                _hasSelection = true;
                _selStartLine = line; _selStartCol = col;
                _selEndLine = line; _selEndCol = col;
                CaptureMouse();
            }
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_selecting) return;
            Point p = e.GetPosition(this);
            int line, col;
            PointToCell(p, out line, out col);
            _selEndLine = line; _selEndCol = col;

            // Auto-scroll when dragging past the edges.
            if (p.Y < PadTop) ScrollByLines(-1);
            else if (p.Y > ActualHeight - PadTop) ScrollByLines(1);

            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_selecting)
            {
                _selecting = false;
                ReleaseMouseCapture();
                if (_selStartLine == _selEndLine && _selStartCol == _selEndCol)
                    _hasSelection = false;
                else if (_settings != null && _settings.CopyOnSelect)
                    CopySelection();
            }
            InvalidateVisual();
        }

        private void PointToCell(Point p, out int line, out int col)
        {
            TerminalBuffer buf = _session.Buffer;
            int rows = RowsForSize;
            int total = buf.TotalLines;
            int firstVisible = _followBottom ? Math.Max(0, total - rows) : _topLine;
            int r = (int)Math.Floor((p.Y - PadTop) / _cellHeight);
            int c = (int)Math.Round((p.X - PadLeft) / _cellWidth);
            if (r < 0) r = 0;
            if (r >= rows) r = rows - 1;
            if (c < 0) c = 0;
            if (c > buf.Cols) c = buf.Cols;
            line = firstVisible + r;
            if (line < 0) line = 0;
            if (line >= total) line = total - 1;
            col = c;
        }

        private void SelectWord(int line, int col)
        {
            TerminalCell[] cells = _session.Buffer.GetLogicalLine(line);
            if (col >= cells.Length) col = cells.Length - 1;
            if (col < 0) return;
            int start = col, end = col;
            while (start > 0 && IsWordChar(cells[start - 1].Char)) start--;
            while (end < cells.Length - 1 && IsWordChar(cells[end + 1].Char)) end++;
            _hasSelection = true;
            _selStartLine = line; _selStartCol = start;
            _selEndLine = line; _selEndCol = end + 1;
            if (_settings != null && _settings.CopyOnSelect) CopySelection();
        }

        private void SelectLine(int line)
        {
            _hasSelection = true;
            _selStartLine = line; _selStartCol = 0;
            _selEndLine = line; _selEndCol = _session.Buffer.Cols;
            if (_settings != null && _settings.CopyOnSelect) CopySelection();
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '/' || c == '\\' || c == ':';
        }

        private bool IsSelected(int line, int col)
        {
            if (!_hasSelection) return false;
            int sLine = _selStartLine, sCol = _selStartCol, eLine = _selEndLine, eCol = _selEndCol;
            Normalize(ref sLine, ref sCol, ref eLine, ref eCol);
            if (line < sLine || line > eLine) return false;
            if (sLine == eLine) return col >= sCol && col < eCol;
            if (line == sLine) return col >= sCol;
            if (line == eLine) return col < eCol;
            return true;
        }

        private static void Normalize(ref int sLine, ref int sCol, ref int eLine, ref int eCol)
        {
            if (sLine > eLine || (sLine == eLine && sCol > eCol))
            {
                int tl = sLine, tc = sCol;
                sLine = eLine; sCol = eCol;
                eLine = tl; eCol = tc;
            }
        }

        public bool HasSelection { get { return _hasSelection; } }

        public string GetSelectedText()
        {
            if (!_hasSelection || _session == null) return "";
            int sLine = _selStartLine, sCol = _selStartCol, eLine = _selEndLine, eCol = _selEndCol;
            Normalize(ref sLine, ref sCol, ref eLine, ref eCol);
            TerminalBuffer buf = _session.Buffer;
            StringBuilder sb = new StringBuilder();
            for (int line = sLine; line <= eLine && line < buf.TotalLines; line++)
            {
                TerminalCell[] cells = buf.GetLogicalLine(line);
                int from = (line == sLine) ? sCol : 0;
                int to = (line == eLine) ? eCol : cells.Length;
                if (from < 0) from = 0;
                if (to > cells.Length) to = cells.Length;
                StringBuilder lineSb = new StringBuilder();
                for (int c = from; c < to; c++) lineSb.Append(cells[c].Char == '\0' ? ' ' : cells[c].Char);
                string s = lineSb.ToString();
                // Trim trailing spaces that are just padding.
                s = s.TrimEnd(' ');
                sb.Append(s);
                if (line != eLine) sb.Append("\r\n");
            }
            return sb.ToString();
        }

        public void CopySelection()
        {
            string text = GetSelectedText();
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch (Exception ex) { Log.Warn("Clipboard copy failed: " + ex.Message); }
        }

        public void ClearSelection()
        {
            _hasSelection = false;
            InvalidateVisual();
        }

        public void Paste()
        {
            if (_session == null) return;
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch (Exception ex) { Log.Warn("Clipboard read failed: " + ex.Message); return; }
            if (string.IsNullOrEmpty(text)) return;

            // Normalise line endings to CR which shells expect from a terminal.
            text = text.Replace("\r\n", "\r").Replace("\n", "\r");

            bool multiline = text.IndexOf('\r') >= 0;
            if (multiline && _settings != null && _settings.MultilinePasteWarning)
            {
                int lineCount = text.Split('\r').Length;
                MessageBoxResult r = MessageBox.Show(
                    Window.GetWindow(this),
                    "You are about to paste " + lineCount + " lines. Each line may run as a command. Continue?",
                    "Nixue CMD - Multiline paste",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (r != MessageBoxResult.OK) return;
            }

            PasteText(text);
        }

        /// <summary>Sends text to the shell as a paste (no clipboard, no warning).</summary>
        public void PasteText(string text)
        {
            if (_session == null || string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r\n", "\r").Replace("\n", "\r");
            if (_session.Emulator.BracketedPaste)
                _session.SendInput("\x1b[200~" + text + "\x1b[201~");
            else
                _session.SendInput(text);
            ScrollToBottom();
        }

        // ---- Keyboard ------------------------------------------------------

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (_session == null) return;
            string text = e.Text;
            if (string.IsNullOrEmpty(text)) return;
            // Ignore control chars here; they are handled in OnPreviewKeyDown.
            if (text.Length == 1 && text[0] < 0x20 && text[0] != 0x1b) return;
            _session.SendInput(text);
            ScrollToBottom();
            e.Handled = true;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (_session == null) return;

            ModifierKeys mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            bool alt = (mods & ModifierKeys.Alt) != 0;

            // Scrollback navigation (Shift + keys) stays local.
            if (shift && e.Key == Key.PageUp) { ScrollByLines(-RowsForSize + 1); e.Handled = true; return; }
            if (shift && e.Key == Key.PageDown) { ScrollByLines(RowsForSize - 1); e.Handled = true; return; }
            if (shift && e.Key == Key.Home) { ScrollByLines(-_session.Buffer.TotalLines); e.Handled = true; return; }
            if (shift && e.Key == Key.End) { ScrollToBottom(); e.Handled = true; return; }

            // Leave app-level shortcuts (Ctrl+Shift+...) to the window.
            if (ctrl && shift) return;

            string seq = MapKey(e.Key, ctrl, alt);
            if (seq != null)
            {
                _session.SendInput(seq);
                ScrollToBottom();
                if (_hasSelection && !(_settings != null && _settings.CopyOnSelect)) ClearSelection();
                e.Handled = true;
            }
        }

        private string MapKey(Key key, bool ctrl, bool alt)
        {
            bool app = _session.Emulator.ApplicationCursorKeys;
            string css = app ? "\x1bO" : "\x1b[";

            switch (key)
            {
                case Key.Enter: return "\r";
                case Key.Back: return "\x7f";
                case Key.Tab: return "\t";
                case Key.Escape: return "\x1b";
                case Key.Up: return css + "A";
                case Key.Down: return css + "B";
                case Key.Right: return css + "C";
                case Key.Left: return css + "D";
                case Key.Home: return app ? "\x1bOH" : "\x1b[H";
                case Key.End: return app ? "\x1bOF" : "\x1b[F";
                case Key.PageUp: return "\x1b[5~";
                case Key.PageDown: return "\x1b[6~";
                case Key.Insert: return "\x1b[2~";
                case Key.Delete: return "\x1b[3~";
                case Key.F1: return "\x1bOP";
                case Key.F2: return "\x1bOQ";
                case Key.F3: return "\x1bOR";
                case Key.F4: return "\x1bOS";
                case Key.F5: return "\x1b[15~";
                case Key.F6: return "\x1b[17~";
                case Key.F7: return "\x1b[18~";
                case Key.F8: return "\x1b[19~";
                case Key.F9: return "\x1b[20~";
                case Key.F10: return "\x1b[21~";
                case Key.F11: return "\x1b[23~";
                case Key.F12: return "\x1b[24~";
            }

            if (ctrl)
            {
                // Ctrl + letter -> control code.
                if (key >= Key.A && key <= Key.Z)
                {
                    int code = (key - Key.A) + 1; // Ctrl+A = 1
                    return ((char)code).ToString();
                }
                switch (key)
                {
                    case Key.Space: return "\x00";
                    case Key.OemOpenBrackets: return "\x1b"; // Ctrl+[
                    case Key.OemCloseBrackets: return "\x1d";
                    case Key.OemBackslash: return "\x1c";
                    case Key.D2: return "\x00";
                    case Key.D6: return "\x1e";
                    case Key.OemMinus: return "\x1f";
                }
            }
            return null;
        }
    }
}
