using System;
using System.Collections.Generic;

namespace Nixue.Core.Config
{
    /// <summary>
    /// A colour theme, expressed as hex strings (<c>#RRGGBB</c>) so the Core library
    /// stays free of any WPF dependency. The UI resolves these to real brushes and
    /// maps the 16 ANSI slots plus the xterm 256-colour cube on top of them.
    /// </summary>
    public sealed class Theme
    {
        public string Name;

        public string AppBackground;
        public string TitleBar;
        public string TabBar;
        public string TerminalBackground;
        public string SecondaryPanel;
        public string Border;
        public string Foreground;
        public string SecondaryForeground;
        public string Accent;
        public string AccentHover;
        public string Success;
        public string Warning;
        public string Error;
        public string Selection;
        public string Cursor;

        /// <summary>16 ANSI colours: 0-7 standard, 8-15 bright.</summary>
        public string[] Ansi;

        public Theme()
        {
            Ansi = new string[16];
        }

        public Theme Clone()
        {
            Theme t = new Theme();
            t.Name = Name;
            t.AppBackground = AppBackground;
            t.TitleBar = TitleBar;
            t.TabBar = TabBar;
            t.TerminalBackground = TerminalBackground;
            t.SecondaryPanel = SecondaryPanel;
            t.Border = Border;
            t.Foreground = Foreground;
            t.SecondaryForeground = SecondaryForeground;
            t.Accent = Accent;
            t.AccentHover = AccentHover;
            t.Success = Success;
            t.Warning = Warning;
            t.Error = Error;
            t.Selection = Selection;
            t.Cursor = Cursor;
            t.Ansi = (string[])Ansi.Clone();
            return t;
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["name"] = Name;
            d["appBackground"] = AppBackground;
            d["titleBar"] = TitleBar;
            d["tabBar"] = TabBar;
            d["terminalBackground"] = TerminalBackground;
            d["secondaryPanel"] = SecondaryPanel;
            d["border"] = Border;
            d["foreground"] = Foreground;
            d["secondaryForeground"] = SecondaryForeground;
            d["accent"] = Accent;
            d["accentHover"] = AccentHover;
            d["success"] = Success;
            d["warning"] = Warning;
            d["error"] = Error;
            d["selection"] = Selection;
            d["cursor"] = Cursor;
            List<object> ansi = new List<object>();
            for (int i = 0; i < Ansi.Length; i++) ansi.Add(Ansi[i]);
            d["ansi"] = ansi;
            return d;
        }

        public static Theme FromJson(Dictionary<string, object> d, Theme fallback)
        {
            if (d == null) return fallback.Clone();
            Theme t = fallback.Clone();
            t.Name = GetStr(d, "name", t.Name);
            t.AppBackground = GetStr(d, "appBackground", t.AppBackground);
            t.TitleBar = GetStr(d, "titleBar", t.TitleBar);
            t.TabBar = GetStr(d, "tabBar", t.TabBar);
            t.TerminalBackground = GetStr(d, "terminalBackground", t.TerminalBackground);
            t.SecondaryPanel = GetStr(d, "secondaryPanel", t.SecondaryPanel);
            t.Border = GetStr(d, "border", t.Border);
            t.Foreground = GetStr(d, "foreground", t.Foreground);
            t.SecondaryForeground = GetStr(d, "secondaryForeground", t.SecondaryForeground);
            t.Accent = GetStr(d, "accent", t.Accent);
            t.AccentHover = GetStr(d, "accentHover", t.AccentHover);
            t.Success = GetStr(d, "success", t.Success);
            t.Warning = GetStr(d, "warning", t.Warning);
            t.Error = GetStr(d, "error", t.Error);
            t.Selection = GetStr(d, "selection", t.Selection);
            t.Cursor = GetStr(d, "cursor", t.Cursor);
            object ansiObj;
            if (d.TryGetValue("ansi", out ansiObj))
            {
                List<object> list = ansiObj as List<object>;
                if (list != null)
                {
                    for (int i = 0; i < 16 && i < list.Count; i++)
                    {
                        string s = list[i] as string;
                        if (!string.IsNullOrEmpty(s)) t.Ansi[i] = s;
                    }
                }
            }
            return t;
        }

        private static string GetStr(Dictionary<string, object> d, string key, string def)
        {
            object v;
            if (d.TryGetValue(key, out v))
            {
                string s = v as string;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return def;
        }
    }
}
