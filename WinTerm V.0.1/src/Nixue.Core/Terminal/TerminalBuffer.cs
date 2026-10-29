using System;
using System.Collections.Generic;

namespace Nixue.Core.Terminal
{
    /// <summary>
    /// The screen model for a terminal: an active grid of <see cref="TerminalCell"/>
    /// rows, a bounded scrollback history, the cursor, scroll region and an alternate
    /// screen. All mutation happens on a single thread (the emulator thread); the UI
    /// reads it on the same thread, so no locking is required here.
    /// </summary>
    public sealed class TerminalBuffer
    {
        private TerminalCell[][] _screen;          // active screen, [row][col]
        private TerminalCell[][] _savedPrimary;    // primary screen saved while on alt
        private readonly List<TerminalCell[]> _scrollback = new List<TerminalCell[]>();

        private int _rows;
        private int _cols;
        private int _scrollbackLimit;

        public int CursorX;
        public int CursorY;
        public bool CursorVisible = true;

        public int ScrollTop;      // inclusive, 0-based
        public int ScrollBottom;   // inclusive, 0-based

        public bool AutoWrap = true;
        public bool OriginMode = false;
        public bool InsertMode = false;
        public bool AltScreenActive = false;

        // Current pen used when filling erased / scrolled-in cells.
        public TermColor CurrentFg = TermColor.Default;
        public TermColor CurrentBg = TermColor.Default;

        // Pending-wrap latch: after writing the last column, the next printable
        // character wraps. Mirrors the "deferred wrap" behaviour of xterm.
        private bool _wrapPending;

        public TerminalBuffer(int rows, int cols, int scrollbackLimit)
        {
            if (rows < 1) rows = 1;
            if (cols < 1) cols = 1;
            _rows = rows;
            _cols = cols;
            _scrollbackLimit = Math.Max(0, scrollbackLimit);
            _screen = AllocScreen(rows, cols);
            ScrollTop = 0;
            ScrollBottom = rows - 1;
        }

        public int Rows { get { return _rows; } }
        public int Cols { get { return _cols; } }
        public int ScrollbackCount { get { return _scrollback.Count; } }
        public int ScrollbackLimit { get { return _scrollbackLimit; } }

        /// <summary>Total number of logical lines addressable for rendering.</summary>
        public int TotalLines { get { return _scrollback.Count + _rows; } }

        private TerminalCell[][] AllocScreen(int rows, int cols)
        {
            TerminalCell[][] s = new TerminalCell[rows][];
            for (int r = 0; r < rows; r++)
            {
                s[r] = NewBlankLine(cols);
            }
            return s;
        }

        private TerminalCell[] NewBlankLine(int cols)
        {
            TerminalCell[] line = new TerminalCell[cols];
            TerminalCell blank = TerminalCell.MakeBlank(CurrentFg, CurrentBg);
            for (int c = 0; c < cols; c++) line[c] = blank;
            return line;
        }

        public void SetScrollbackLimit(int limit)
        {
            _scrollbackLimit = Math.Max(0, limit);
            TrimScrollback();
        }

        private void TrimScrollback()
        {
            int excess = _scrollback.Count - _scrollbackLimit;
            if (excess > 0) _scrollback.RemoveRange(0, excess);
        }

        /// <summary>Returns a line for rendering. 0.._scrollback.Count-1 are history,
        /// then the active screen rows follow.</summary>
        public TerminalCell[] GetLogicalLine(int absoluteIndex)
        {
            if (absoluteIndex < 0) return NewBlankLine(_cols);
            if (absoluteIndex < _scrollback.Count) return _scrollback[absoluteIndex];
            int sr = absoluteIndex - _scrollback.Count;
            if (sr >= 0 && sr < _rows) return _screen[sr];
            return NewBlankLine(_cols);
        }

        public TerminalCell[] GetScreenRow(int row)
        {
            if (row < 0 || row >= _rows) return NewBlankLine(_cols);
            return _screen[row];
        }

        // ---- Cursor helpers -------------------------------------------------

        private int RegionTop { get { return OriginMode ? ScrollTop : 0; } }
        private int RegionBottom { get { return OriginMode ? ScrollBottom : _rows - 1; } }

        public void SetCursor(int x, int y)
        {
            int top = RegionTop;
            int bottom = RegionBottom;
            CursorY = Clamp(top + y, top, bottom);
            if (!OriginMode) CursorY = Clamp(y, 0, _rows - 1);
            CursorX = Clamp(x, 0, _cols - 1);
            _wrapPending = false;
        }

        public void SetCursorAbsolute(int x, int y)
        {
            CursorX = Clamp(x, 0, _cols - 1);
            CursorY = Clamp(y, 0, _rows - 1);
            _wrapPending = false;
        }

        public void MoveCursor(int dx, int dy)
        {
            CursorX = Clamp(CursorX + dx, 0, _cols - 1);
            CursorY = Clamp(CursorY + dy, 0, _rows - 1);
            _wrapPending = false;
        }

        public void CarriageReturn()
        {
            CursorX = 0;
            _wrapPending = false;
        }

        public void Backspace()
        {
            if (_wrapPending) { _wrapPending = false; return; }
            if (CursorX > 0) CursorX--;
        }

        public void Tab(int tabWidth)
        {
            if (tabWidth < 1) tabWidth = 8;
            int next = ((CursorX / tabWidth) + 1) * tabWidth;
            CursorX = Clamp(next, 0, _cols - 1);
            _wrapPending = false;
        }

        /// <summary>Line feed honouring the scroll region; scrolls and feeds scrollback.</summary>
        public void LineFeed()
        {
            if (CursorY == ScrollBottom)
            {
                ScrollUp(1);
            }
            else if (CursorY < _rows - 1)
            {
                CursorY++;
            }
            _wrapPending = false;
        }

        public void ReverseLineFeed()
        {
            if (CursorY == ScrollTop)
            {
                ScrollDown(1);
            }
            else if (CursorY > 0)
            {
                CursorY--;
            }
            _wrapPending = false;
        }

        // ---- Printing -------------------------------------------------------

        public void Print(char ch, CellFlags flags)
        {
            if (_wrapPending && AutoWrap)
            {
                CursorX = 0;
                LineFeed();
                _wrapPending = false;
            }

            if (InsertMode)
            {
                TerminalCell[] row0 = _screen[CursorY];
                for (int c = _cols - 1; c > CursorX; c--) row0[c] = row0[c - 1];
            }

            TerminalCell cell = new TerminalCell();
            cell.Char = ch;
            cell.Fg = CurrentFg;
            cell.Bg = CurrentBg;
            cell.Flags = flags;
            _screen[CursorY][CursorX] = cell;

            if (CursorX == _cols - 1)
            {
                // Defer the wrap until the next printable character arrives.
                _wrapPending = AutoWrap;
            }
            else
            {
                CursorX++;
            }
        }

        // ---- Scrolling ------------------------------------------------------

        public void ScrollUp(int n)
        {
            if (n < 1) return;
            int top = ScrollTop, bottom = ScrollBottom;
            int regionHeight = bottom - top + 1;
            if (n > regionHeight) n = regionHeight;

            bool wholeScreen = (top == 0 && bottom == _rows - 1);
            for (int i = 0; i < n; i++)
            {
                if (wholeScreen && !AltScreenActive)
                {
                    _scrollback.Add(_screen[top]);
                    TrimScrollback();
                }
                // Shift lines up within the region.
                for (int r = top; r < bottom; r++) _screen[r] = _screen[r + 1];
                _screen[bottom] = NewBlankLine(_cols);
            }
        }

        public void ScrollDown(int n)
        {
            if (n < 1) return;
            int top = ScrollTop, bottom = ScrollBottom;
            int regionHeight = bottom - top + 1;
            if (n > regionHeight) n = regionHeight;
            for (int i = 0; i < n; i++)
            {
                for (int r = bottom; r > top; r--) _screen[r] = _screen[r - 1];
                _screen[top] = NewBlankLine(_cols);
            }
        }

        // ---- Erase / insert / delete ---------------------------------------

        private TerminalCell BlankCell()
        {
            return TerminalCell.MakeBlank(CurrentFg, CurrentBg);
        }

        public void EraseInLine(int mode)
        {
            TerminalCell blank = BlankCell();
            TerminalCell[] row = _screen[CursorY];
            if (mode == 0) { for (int c = CursorX; c < _cols; c++) row[c] = blank; }
            else if (mode == 1) { for (int c = 0; c <= CursorX && c < _cols; c++) row[c] = blank; }
            else if (mode == 2) { for (int c = 0; c < _cols; c++) row[c] = blank; }
            _wrapPending = false;
        }

        public void EraseInDisplay(int mode)
        {
            TerminalCell blank = BlankCell();
            if (mode == 0)
            {
                EraseInLine(0);
                for (int r = CursorY + 1; r < _rows; r++) FillRow(r, blank);
            }
            else if (mode == 1)
            {
                for (int r = 0; r < CursorY; r++) FillRow(r, blank);
                EraseInLine(1);
            }
            else if (mode == 2 || mode == 3)
            {
                for (int r = 0; r < _rows; r++) FillRow(r, blank);
            }
            _wrapPending = false;
        }

        private void FillRow(int r, TerminalCell blank)
        {
            TerminalCell[] row = _screen[r];
            for (int c = 0; c < _cols; c++) row[c] = blank;
        }

        public void EraseChars(int n)
        {
            if (n < 1) n = 1;
            TerminalCell blank = BlankCell();
            TerminalCell[] row = _screen[CursorY];
            for (int i = 0; i < n && CursorX + i < _cols; i++) row[CursorX + i] = blank;
            _wrapPending = false;
        }

        public void InsertChars(int n)
        {
            if (n < 1) n = 1;
            TerminalCell blank = BlankCell();
            TerminalCell[] row = _screen[CursorY];
            for (int c = _cols - 1; c >= CursorX; c--)
            {
                int src = c - n;
                row[c] = (src >= CursorX) ? row[src] : blank;
            }
            _wrapPending = false;
        }

        public void DeleteChars(int n)
        {
            if (n < 1) n = 1;
            TerminalCell blank = BlankCell();
            TerminalCell[] row = _screen[CursorY];
            for (int c = CursorX; c < _cols; c++)
            {
                int src = c + n;
                row[c] = (src < _cols) ? row[src] : blank;
            }
            _wrapPending = false;
        }

        public void InsertLines(int n)
        {
            if (CursorY < ScrollTop || CursorY > ScrollBottom) return;
            if (n < 1) n = 1;
            int count = Math.Min(n, ScrollBottom - CursorY + 1);
            for (int i = 0; i < count; i++)
            {
                for (int r = ScrollBottom; r > CursorY; r--) _screen[r] = _screen[r - 1];
                _screen[CursorY] = NewBlankLine(_cols);
            }
            _wrapPending = false;
        }

        public void DeleteLines(int n)
        {
            if (CursorY < ScrollTop || CursorY > ScrollBottom) return;
            if (n < 1) n = 1;
            int count = Math.Min(n, ScrollBottom - CursorY + 1);
            for (int i = 0; i < count; i++)
            {
                for (int r = CursorY; r < ScrollBottom; r++) _screen[r] = _screen[r + 1];
                _screen[ScrollBottom] = NewBlankLine(_cols);
            }
            _wrapPending = false;
        }

        public void SetScrollRegion(int top, int bottom)
        {
            if (top < 0) top = 0;
            if (bottom < 0 || bottom >= _rows) bottom = _rows - 1;
            if (top >= bottom) { top = 0; bottom = _rows - 1; }
            ScrollTop = top;
            ScrollBottom = bottom;
            // Per DECSTBM the cursor returns to home.
            if (OriginMode) SetCursor(0, 0); else SetCursorAbsolute(0, 0);
        }

        public void ResetScrollRegion()
        {
            ScrollTop = 0;
            ScrollBottom = _rows - 1;
        }

        // ---- Alternate screen ----------------------------------------------

        public void EnterAltScreen(bool clear)
        {
            if (AltScreenActive) return;
            _savedPrimary = _screen;
            _screen = AllocScreen(_rows, _cols);
            AltScreenActive = true;
            ResetScrollRegion();
            if (clear) EraseInDisplay(2);
        }

        public void LeaveAltScreen()
        {
            if (!AltScreenActive) return;
            _screen = _savedPrimary != null ? _savedPrimary : AllocScreen(_rows, _cols);
            _savedPrimary = null;
            AltScreenActive = false;
            ResetScrollRegion();
        }

        // ---- Resize ---------------------------------------------------------

        public void Resize(int newRows, int newCols)
        {
            if (newRows < 1) newRows = 1;
            if (newCols < 1) newCols = 1;
            if (newRows == _rows && newCols == _cols) return;

            _screen = ResizeGrid(_screen, newRows, newCols);
            if (_savedPrimary != null) _savedPrimary = ResizeGrid(_savedPrimary, newRows, newCols);

            // Clamp scrollback lines to the new width.
            for (int i = 0; i < _scrollback.Count; i++)
                _scrollback[i] = ResizeLine(_scrollback[i], newCols);

            _rows = newRows;
            _cols = newCols;
            ScrollTop = 0;
            ScrollBottom = _rows - 1;
            CursorX = Clamp(CursorX, 0, _cols - 1);
            CursorY = Clamp(CursorY, 0, _rows - 1);
            _wrapPending = false;
        }

        private TerminalCell[][] ResizeGrid(TerminalCell[][] grid, int newRows, int newCols)
        {
            TerminalCell[][] ng = new TerminalCell[newRows][];
            int oldRows = grid.Length;
            // Keep the bottom-most rows (most recent) when shrinking vertically.
            int copyRows = Math.Min(oldRows, newRows);
            int srcStart = oldRows - copyRows;
            for (int r = 0; r < newRows; r++)
            {
                if (r < copyRows) ng[r] = ResizeLine(grid[srcStart + r], newCols);
                else ng[r] = NewBlankLine(newCols);
            }
            return ng;
        }

        private TerminalCell[] ResizeLine(TerminalCell[] line, int newCols)
        {
            if (line.Length == newCols) return line;
            TerminalCell[] nl = NewBlankLine(newCols);
            int copy = Math.Min(line.Length, newCols);
            Array.Copy(line, nl, copy);
            return nl;
        }

        // ---- Full reset -----------------------------------------------------

        public void HardReset()
        {
            _scrollback.Clear();
            _savedPrimary = null;
            AltScreenActive = false;
            CurrentFg = TermColor.Default;
            CurrentBg = TermColor.Default;
            AutoWrap = true;
            OriginMode = false;
            InsertMode = false;
            CursorVisible = true;
            _screen = AllocScreen(_rows, _cols);
            ResetScrollRegion();
            SetCursorAbsolute(0, 0);
        }

        public void ClearScrollback()
        {
            _scrollback.Clear();
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
