using System;

namespace Nixue.Core.Terminal
{
    /// <summary>
    /// How a <see cref="TermColor"/> should be resolved against a palette.
    /// </summary>
    public enum ColorKind : byte
    {
        /// <summary>Use the terminal's default foreground or background.</summary>
        Default = 0,
        /// <summary>Use a slot from the 256-color palette (index stored in R).</summary>
        Indexed = 1,
        /// <summary>Use the literal R/G/B triple ("true color").</summary>
        Rgb = 2
    }

    /// <summary>
    /// A terminal color reference. Kept small and value-typed so cells stay cheap.
    /// It intentionally does not depend on WPF; the UI layer resolves it to a real
    /// color against the active theme palette.
    /// </summary>
    public struct TermColor : IEquatable<TermColor>
    {
        public ColorKind Kind;
        public byte R;
        public byte G;
        public byte B;

        public static readonly TermColor Default = new TermColor();

        public static TermColor FromIndex(int index)
        {
            if (index < 0) index = 0;
            if (index > 255) index = 255;
            TermColor c = new TermColor();
            c.Kind = ColorKind.Indexed;
            c.R = (byte)index;
            return c;
        }

        public static TermColor FromRgb(byte r, byte g, byte b)
        {
            TermColor c = new TermColor();
            c.Kind = ColorKind.Rgb;
            c.R = r;
            c.G = g;
            c.B = b;
            return c;
        }

        /// <summary>For an indexed color, the palette slot (0-255).</summary>
        public int Index { get { return R; } }

        public bool IsDefault { get { return Kind == ColorKind.Default; } }

        public bool Equals(TermColor other)
        {
            return Kind == other.Kind && R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is TermColor && Equals((TermColor)obj);
        }

        public override int GetHashCode()
        {
            return ((int)Kind << 24) ^ (R << 16) ^ (G << 8) ^ B;
        }
    }
}
