using System;
using System.Collections.Generic;

namespace Nixue.Core.Config
{
    /// <summary>The built-in themes. "Nixue Dark" is the default.</summary>
    public static class ThemeLibrary
    {
        public const string DefaultThemeName = "Nixue Dark";

        public static List<Theme> BuiltIn()
        {
            List<Theme> list = new List<Theme>();
            list.Add(NixueDark());
            list.Add(Midnight());
            list.Add(Monochrome());
            list.Add(HighContrast());
            return list;
        }

        public static Theme GetByName(string name)
        {
            List<Theme> all = BuiltIn();
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return NixueDark();
        }

        public static Theme Default() { return NixueDark(); }

        public static Theme NixueDark()
        {
            Theme t = new Theme();
            t.Name = "Nixue Dark";
            t.AppBackground = "#090B10";
            t.TitleBar = "#0C0F15";
            t.TabBar = "#0D1118";
            t.TerminalBackground = "#080A0E";
            t.SecondaryPanel = "#10141C";
            t.Border = "#252C38";
            t.Foreground = "#E6EAF2";
            t.SecondaryForeground = "#858FA3";
            t.Accent = "#7AA2F7";
            t.AccentHover = "#9BB9FF";
            t.Success = "#9ECE6A";
            t.Warning = "#E0AF68";
            t.Error = "#F7768E";
            t.Selection = "#2A3A5E";
            t.Cursor = "#7AA2F7";
            t.Ansi = new string[]
            {
                "#15181F", "#F7768E", "#9ECE6A", "#E0AF68",
                "#7AA2F7", "#BB9AF7", "#7DCFFF", "#A9B1D6",
                "#414868", "#FF899D", "#B9F27C", "#FFC777",
                "#9BB9FF", "#C9AFFF", "#A6E3FF", "#E6EAF2"
            };
            return t;
        }

        public static Theme Midnight()
        {
            Theme t = new Theme();
            t.Name = "Midnight";
            t.AppBackground = "#05060A";
            t.TitleBar = "#070912";
            t.TabBar = "#080B14";
            t.TerminalBackground = "#04050A";
            t.SecondaryPanel = "#0B0F1A";
            t.Border = "#1A2030";
            t.Foreground = "#D7DEEC";
            t.SecondaryForeground = "#6B7488";
            t.Accent = "#5CA8FF";
            t.AccentHover = "#86C2FF";
            t.Success = "#73D08E";
            t.Warning = "#E6B455";
            t.Error = "#F26D82";
            t.Selection = "#1E2C4A";
            t.Cursor = "#5CA8FF";
            t.Ansi = new string[]
            {
                "#0D1019", "#F26D82", "#73D08E", "#E6B455",
                "#5CA8FF", "#A98CF0", "#5FD0F0", "#9AA4BC",
                "#2A3350", "#FF8095", "#8CE0A5", "#F2C76B",
                "#86C2FF", "#C0A6FF", "#8CE0FF", "#D7DEEC"
            };
            return t;
        }

        public static Theme Monochrome()
        {
            Theme t = new Theme();
            t.Name = "Monochrome";
            t.AppBackground = "#0A0A0A";
            t.TitleBar = "#0E0E0E";
            t.TabBar = "#101010";
            t.TerminalBackground = "#070707";
            t.SecondaryPanel = "#141414";
            t.Border = "#2A2A2A";
            t.Foreground = "#D8D8D8";
            t.SecondaryForeground = "#7A7A7A";
            t.Accent = "#BFBFBF";
            t.AccentHover = "#E8E8E8";
            t.Success = "#B8B8B8";
            t.Warning = "#C8C8C8";
            t.Error = "#E0E0E0";
            t.Selection = "#333333";
            t.Cursor = "#D8D8D8";
            t.Ansi = new string[]
            {
                "#1A1A1A", "#8A8A8A", "#A0A0A0", "#B0B0B0",
                "#909090", "#A8A8A8", "#B8B8B8", "#C8C8C8",
                "#3A3A3A", "#A6A6A6", "#BCBCBC", "#CCCCCC",
                "#ACACAC", "#C4C4C4", "#D4D4D4", "#F0F0F0"
            };
            return t;
        }

        public static Theme HighContrast()
        {
            Theme t = new Theme();
            t.Name = "High Contrast";
            t.AppBackground = "#000000";
            t.TitleBar = "#000000";
            t.TabBar = "#000000";
            t.TerminalBackground = "#000000";
            t.SecondaryPanel = "#0A0A0A";
            t.Border = "#FFFFFF";
            t.Foreground = "#FFFFFF";
            t.SecondaryForeground = "#C0C0C0";
            t.Accent = "#00E5FF";
            t.AccentHover = "#66F0FF";
            t.Success = "#00FF66";
            t.Warning = "#FFD400";
            t.Error = "#FF3355";
            t.Selection = "#005577";
            t.Cursor = "#00E5FF";
            t.Ansi = new string[]
            {
                "#000000", "#FF3355", "#00FF66", "#FFD400",
                "#00AAFF", "#D070FF", "#00E5FF", "#FFFFFF",
                "#555555", "#FF6677", "#55FF99", "#FFE866",
                "#66C2FF", "#E0A0FF", "#66F0FF", "#FFFFFF"
            };
            return t;
        }
    }
}
