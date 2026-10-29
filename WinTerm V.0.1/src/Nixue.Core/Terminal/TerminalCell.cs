using System;

namespace Nixue.Core.Terminal
{
    /// <summary>Per-cell rendering attributes (SGR state).</summary>
    [Flags]
    public enum CellFlags : ushort
    {
        None      = 0,
        Bold      = 1 << 0,
        Faint     = 1 << 1,
        Italic    = 1 << 2,
        Underline = 1 << 3,
        Blink     = 1 << 4,
        Inverse   = 1 << 5,
        Hidden    = 1 << 6,
        Strike    = 1 << 7
    }

    /// <summary>
    /// A single character cell: the glyph plus its foreground, background and flags.
    /// Value type so a screen buffer is a flat array with no per-cell allocation.
    /// </summary>
    public struct TerminalCell : IEquatable<TerminalCell>
    {
        public char Char;
        public TermColor Fg;
        public TermColor Bg;
        public CellFlags Flags;

        public static readonly TerminalCell Blank = MakeBlank(TermColor.Default, TermColor.Default);

        public static TerminalCell MakeBlank(TermColor fg, TermColor bg)
        {
            TerminalCell c = new TerminalCell();
            c.Char = ' ';
            c.Fg = fg;
            c.Bg = bg;
            c.Flags = CellFlags.None;
            return c;
        }

        public bool IsBlank
        {
            get { return (Char == ' ' || Char == '\0') && Flags == CellFlags.None; }
        }

        public bool Equals(TerminalCell other)
        {
            return Char == other.Char
                && Flags == other.Flags
                && Fg.Equals(other.Fg)
                && Bg.Equals(other.Bg);
        }

        public override bool Equals(object obj)
        {
            return obj is TerminalCell && Equals((TerminalCell)obj);
        }

        public override int GetHashCode()
        {
            return Char.GetHashCode() ^ ((int)Flags << 16) ^ Fg.GetHashCode() ^ Bg.GetHashCode();
        }
    }
}
