using System;
using System.Collections.Generic;
using System.Windows.Media;
using Nixue.Core.Config;
using Nixue.Core.Terminal;

namespace Nixue.App.UI
{
    /// <summary>
    /// Resolves a Core <see cref="Theme"/> (hex strings, WPF-free) into cached WPF
    /// brushes and colours, and maps terminal <see cref="TermColor"/> values (ANSI 16,
    /// the xterm 256-colour cube and true colour) onto real colours.
    /// </summary>
    public sealed class ThemeResources
    {
        public Theme Theme { get; private set; }

        public Color AppBackground, TitleBar, TabBar, TerminalBackground, SecondaryPanel;
        public Color Border, Foreground, SecondaryForeground, Accent, AccentHover;
        public Color Success, Warning, Error, Selection, Cursor;

        public Brush AppBackgroundBrush, TitleBarBrush, TabBarBrush, TerminalBackgroundBrush, SecondaryPanelBrush;
        public Brush BorderBrush, ForegroundBrush, SecondaryForegroundBrush, AccentBrush, AccentHoverBrush;
        public Brush SelectionBrush, CursorBrush, ErrorBrush, SuccessBrush, WarningBrush;

        private readonly Color[] _ansi = new Color[16];
        private readonly Color[] _cube = new Color[256];
        private readonly Dictionary<uint, Brush> _brushCache = new Dictionary<uint, Brush>();

        public ThemeResources(Theme theme)
        {
            Apply(theme);
        }

        public void Apply(Theme theme)
        {
            Theme = theme;
            AppBackground = Parse(theme.AppBackground, Colors.Black);
            TitleBar = Parse(theme.TitleBar, Colors.Black);
            TabBar = Parse(theme.TabBar, Colors.Black);
            TerminalBackground = Parse(theme.TerminalBackground, Colors.Black);
            SecondaryPanel = Parse(theme.SecondaryPanel, Colors.Black);
            Border = Parse(theme.Border, Colors.Gray);
            Foreground = Parse(theme.Foreground, Colors.White);
            SecondaryForeground = Parse(theme.SecondaryForeground, Colors.LightGray);
            Accent = Parse(theme.Accent, Colors.CornflowerBlue);
            AccentHover = Parse(theme.AccentHover, Colors.LightBlue);
            Success = Parse(theme.Success, Colors.LightGreen);
            Warning = Parse(theme.Warning, Colors.Orange);
            Error = Parse(theme.Error, Colors.IndianRed);
            Selection = Parse(theme.Selection, Color.FromArgb(120, 70, 110, 200));
            Cursor = Parse(theme.Cursor, Accent);

            for (int i = 0; i < 16; i++)
                _ansi[i] = Parse(theme.Ansi != null && i < theme.Ansi.Length ? theme.Ansi[i] : null, FallbackAnsi(i));

            BuildCube();

            AppBackgroundBrush = Freeze(new SolidColorBrush(AppBackground));
            TitleBarBrush = Freeze(new SolidColorBrush(TitleBar));
            TabBarBrush = Freeze(new SolidColorBrush(TabBar));
            TerminalBackgroundBrush = Freeze(new SolidColorBrush(TerminalBackground));
            SecondaryPanelBrush = Freeze(new SolidColorBrush(SecondaryPanel));
            BorderBrush = Freeze(new SolidColorBrush(Border));
            ForegroundBrush = Freeze(new SolidColorBrush(Foreground));
            SecondaryForegroundBrush = Freeze(new SolidColorBrush(SecondaryForeground));
            AccentBrush = Freeze(new SolidColorBrush(Accent));
            AccentHoverBrush = Freeze(new SolidColorBrush(AccentHover));
            ErrorBrush = Freeze(new SolidColorBrush(Error));
            SuccessBrush = Freeze(new SolidColorBrush(Success));
            WarningBrush = Freeze(new SolidColorBrush(Warning));
            CursorBrush = Freeze(new SolidColorBrush(Cursor));

            Color sel = Selection;
            if (sel.A == 255) sel = Color.FromArgb(150, sel.R, sel.G, sel.B);
            SelectionBrush = Freeze(new SolidColorBrush(sel));

            _brushCache.Clear();
        }

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public Color ResolveColor(TermColor c, bool isBackground)
        {
            switch (c.Kind)
            {
                case ColorKind.Rgb:
                    return Color.FromRgb(c.R, c.G, c.B);
                case ColorKind.Indexed:
                    return _cube[c.Index];
                default:
                    return isBackground ? TerminalBackground : Foreground;
            }
        }

        /// <summary>Cached brush for a resolved colour (used for text runs).</summary>
        public Brush BrushFor(Color c)
        {
            uint key = (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
            Brush b;
            if (_brushCache.TryGetValue(key, out b)) return b;
            SolidColorBrush s = new SolidColorBrush(c);
            s.Freeze();
            _brushCache[key] = s;
            return s;
        }

        private void BuildCube()
        {
            for (int i = 0; i < 16; i++) _cube[i] = _ansi[i];
            int idx = 16;
            int[] steps = new int[] { 0, 95, 135, 175, 215, 255 };
            for (int r = 0; r < 6; r++)
                for (int g = 0; g < 6; g++)
                    for (int b = 0; b < 6; b++)
                        _cube[idx++] = Color.FromRgb((byte)steps[r], (byte)steps[g], (byte)steps[b]);
            for (int i = 0; i < 24; i++)
            {
                byte v = (byte)(8 + i * 10);
                _cube[232 + i] = Color.FromRgb(v, v, v);
            }
        }

        private static Color FallbackAnsi(int i)
        {
            Color[] basic = new Color[]
            {
                Color.FromRgb(0x00,0x00,0x00), Color.FromRgb(0xCD,0x31,0x31),
                Color.FromRgb(0x0D,0xBC,0x79), Color.FromRgb(0xE5,0xE5,0x10),
                Color.FromRgb(0x24,0x72,0xC8), Color.FromRgb(0xBC,0x3F,0xBC),
                Color.FromRgb(0x11,0xA8,0xCD), Color.FromRgb(0xE5,0xE5,0xE5),
                Color.FromRgb(0x66,0x66,0x66), Color.FromRgb(0xF1,0x4C,0x4C),
                Color.FromRgb(0x23,0xD1,0x8B), Color.FromRgb(0xF5,0xF5,0x43),
                Color.FromRgb(0x3B,0x8E,0xEA), Color.FromRgb(0xD6,0x70,0xD6),
                Color.FromRgb(0x29,0xB8,0xDB), Color.FromRgb(0xFF,0xFF,0xFF)
            };
            return basic[i & 15];
        }

        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            try
            {
                string s = hex.Trim();
                if (s.StartsWith("#")) s = s.Substring(1);
                if (s.Length == 6)
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    return Color.FromRgb(r, g, b);
                }
                if (s.Length == 8)
                {
                    byte a = Convert.ToByte(s.Substring(0, 2), 16);
                    byte r = Convert.ToByte(s.Substring(2, 2), 16);
                    byte g = Convert.ToByte(s.Substring(4, 2), 16);
                    byte b = Convert.ToByte(s.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch { }
            return fallback;
        }
    }
}
