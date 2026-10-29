using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nixue.Core.Config
{
    /// <summary>Which shell a session should launch.</summary>
    public enum ShellKind
    {
        Cmd = 0,
        WindowsPowerShell = 1,
        PowerShell7 = 2
    }

    /// <summary>Visual style of the rendered cursor.</summary>
    public enum CursorStyle
    {
        Block = 0,
        Beam = 1,
        Underline = 2
    }

    /// <summary>
    /// The complete, serialisable application configuration. Serialisation is explicit
    /// (no reflection) so that a settings file written by an older build still loads:
    /// any missing key falls back to the default, and invalid values are clamped by
    /// <see cref="Validate"/>.
    /// </summary>
    public sealed class AppSettings
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        // Appearance
        public string ThemeName;
        public string InterfaceFont;
        public string TerminalFont;
        public string TerminalFontFile;   // absolute path to an uploaded .ttf, or ""
        public double TerminalFontSize;
        public double LineSpacing;
        public bool ShowTabBar;
        public bool ShowStatusBar;
        public double WindowOpacity;
        public bool ReducedAnimation;

        // Terminal
        public ShellKind DefaultShell;
        public string StartingDirectory;
        public bool CustomPromptEnabled;
        public string PromptSymbol;
        public bool ShowPathInPrompt;
        public CursorStyle CursorStyle;
        public bool CursorBlink;
        public int CursorBlinkRateMs;
        public int ScrollbackLimit;
        public bool BellEnabled;
        public bool CopyOnSelect;
        public bool MultilinePasteWarning;
        public bool NewTabInCurrentDirectory;
        public bool LoadPowerShellProfile;

        // Behaviour
        public bool ConfirmCloseRunningTab;
        public bool NewTabWhenLastClosed;   // false => close the application
        public bool RestoreWindowBounds;
        public bool StartMaximized;
        public bool StartFullscreen;
        public double WindowLeft;
        public double WindowTop;
        public double WindowWidth;
        public double WindowHeight;
        public bool WindowMaximized;

        // Advanced
        public bool DiagnosticLogging;
        public string LogVerbosity;         // "error" | "warn" | "info" | "debug"

        public AppSettings()
        {
            ResetToDefaults();
        }

        public void ResetToDefaults()
        {
            SchemaVersion = CurrentSchemaVersion;

            ThemeName = ThemeLibrary.DefaultThemeName;
            InterfaceFont = "Segoe UI";
            TerminalFont = "Cascadia Mono, JetBrains Mono, Consolas";
            TerminalFontFile = "";
            TerminalFontSize = 14.0;
            LineSpacing = 1.0;
            ShowTabBar = true;
            ShowStatusBar = false;
            WindowOpacity = 1.0;
            ReducedAnimation = false;

            DefaultShell = ShellKind.Cmd;
            StartingDirectory = "";
            CustomPromptEnabled = true;
            PromptSymbol = ">";
            ShowPathInPrompt = true;
            CursorStyle = CursorStyle.Block;
            CursorBlink = true;
            CursorBlinkRateMs = 530;
            ScrollbackLimit = 5000;
            BellEnabled = false;
            CopyOnSelect = false;
            MultilinePasteWarning = true;
            NewTabInCurrentDirectory = false;
            LoadPowerShellProfile = false;

            ConfirmCloseRunningTab = true;
            NewTabWhenLastClosed = false;
            RestoreWindowBounds = true;
            StartMaximized = false;
            StartFullscreen = false;
            WindowLeft = double.NaN;
            WindowTop = double.NaN;
            WindowWidth = 1100;
            WindowHeight = 680;
            WindowMaximized = false;

            DiagnosticLogging = false;
            LogVerbosity = "info";
        }

        /// <summary>Clamp every value into a safe range. Always leaves a usable object.</summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(ThemeName)) ThemeName = ThemeLibrary.DefaultThemeName;
            if (string.IsNullOrEmpty(InterfaceFont)) InterfaceFont = "Segoe UI";
            if (string.IsNullOrEmpty(TerminalFont)) TerminalFont = "Consolas";
            if (TerminalFontFile == null) TerminalFontFile = "";
            TerminalFontSize = ClampD(TerminalFontSize, 6.0, 72.0, 14.0);
            LineSpacing = ClampD(LineSpacing, 0.8, 2.0, 1.0);
            WindowOpacity = ClampD(WindowOpacity, 0.3, 1.0, 1.0);

            if (string.IsNullOrEmpty(PromptSymbol)) PromptSymbol = ">";
            if (PromptSymbol.Length > 8) PromptSymbol = PromptSymbol.Substring(0, 8);
            CursorBlinkRateMs = ClampI(CursorBlinkRateMs, 100, 2000, 530);
            ScrollbackLimit = ClampI(ScrollbackLimit, 0, 200000, 5000);

            WindowWidth = ClampD(WindowWidth, 480, 20000, 1100);
            WindowHeight = ClampD(WindowHeight, 320, 20000, 680);

            if (LogVerbosity != "error" && LogVerbosity != "warn" &&
                LogVerbosity != "info" && LogVerbosity != "debug")
                LogVerbosity = "info";

            SchemaVersion = CurrentSchemaVersion;
        }

        public AppSettings Clone()
        {
            return FromJson(ToJson());
        }

        // ---- JSON -----------------------------------------------------------

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["schemaVersion"] = SchemaVersion;

            d["themeName"] = ThemeName;
            d["interfaceFont"] = InterfaceFont;
            d["terminalFont"] = TerminalFont;
            d["terminalFontFile"] = TerminalFontFile;
            d["terminalFontSize"] = TerminalFontSize;
            d["lineSpacing"] = LineSpacing;
            d["showTabBar"] = ShowTabBar;
            d["showStatusBar"] = ShowStatusBar;
            d["windowOpacity"] = WindowOpacity;
            d["reducedAnimation"] = ReducedAnimation;

            d["defaultShell"] = DefaultShell.ToString();
            d["startingDirectory"] = StartingDirectory;
            d["customPromptEnabled"] = CustomPromptEnabled;
            d["promptSymbol"] = PromptSymbol;
            d["showPathInPrompt"] = ShowPathInPrompt;
            d["cursorStyle"] = CursorStyle.ToString();
            d["cursorBlink"] = CursorBlink;
            d["cursorBlinkRateMs"] = CursorBlinkRateMs;
            d["scrollbackLimit"] = ScrollbackLimit;
            d["bellEnabled"] = BellEnabled;
            d["copyOnSelect"] = CopyOnSelect;
            d["multilinePasteWarning"] = MultilinePasteWarning;
            d["newTabInCurrentDirectory"] = NewTabInCurrentDirectory;
            d["loadPowerShellProfile"] = LoadPowerShellProfile;

            d["confirmCloseRunningTab"] = ConfirmCloseRunningTab;
            d["newTabWhenLastClosed"] = NewTabWhenLastClosed;
            d["restoreWindowBounds"] = RestoreWindowBounds;
            d["startMaximized"] = StartMaximized;
            d["startFullscreen"] = StartFullscreen;
            d["windowLeft"] = double.IsNaN(WindowLeft) ? (object)null : WindowLeft;
            d["windowTop"] = double.IsNaN(WindowTop) ? (object)null : WindowTop;
            d["windowWidth"] = WindowWidth;
            d["windowHeight"] = WindowHeight;
            d["windowMaximized"] = WindowMaximized;

            d["diagnosticLogging"] = DiagnosticLogging;
            d["logVerbosity"] = LogVerbosity;
            return d;
        }

        public static AppSettings FromJson(Dictionary<string, object> d)
        {
            AppSettings s = new AppSettings();
            if (d == null) { return s; }

            s.SchemaVersion = GetI(d, "schemaVersion", s.SchemaVersion);

            s.ThemeName = GetS(d, "themeName", s.ThemeName);
            s.InterfaceFont = GetS(d, "interfaceFont", s.InterfaceFont);
            s.TerminalFont = GetS(d, "terminalFont", s.TerminalFont);
            s.TerminalFontFile = GetS(d, "terminalFontFile", s.TerminalFontFile);
            s.TerminalFontSize = GetD(d, "terminalFontSize", s.TerminalFontSize);
            s.LineSpacing = GetD(d, "lineSpacing", s.LineSpacing);
            s.ShowTabBar = GetB(d, "showTabBar", s.ShowTabBar);
            s.ShowStatusBar = GetB(d, "showStatusBar", s.ShowStatusBar);
            s.WindowOpacity = GetD(d, "windowOpacity", s.WindowOpacity);
            s.ReducedAnimation = GetB(d, "reducedAnimation", s.ReducedAnimation);

            s.DefaultShell = ParseShell(GetS(d, "defaultShell", s.DefaultShell.ToString()), s.DefaultShell);
            s.StartingDirectory = GetS(d, "startingDirectory", s.StartingDirectory);
            s.CustomPromptEnabled = GetB(d, "customPromptEnabled", s.CustomPromptEnabled);
            s.PromptSymbol = GetS(d, "promptSymbol", s.PromptSymbol);
            s.ShowPathInPrompt = GetB(d, "showPathInPrompt", s.ShowPathInPrompt);
            s.CursorStyle = ParseCursor(GetS(d, "cursorStyle", s.CursorStyle.ToString()), s.CursorStyle);
            s.CursorBlink = GetB(d, "cursorBlink", s.CursorBlink);
            s.CursorBlinkRateMs = GetI(d, "cursorBlinkRateMs", s.CursorBlinkRateMs);
            s.ScrollbackLimit = GetI(d, "scrollbackLimit", s.ScrollbackLimit);
            s.BellEnabled = GetB(d, "bellEnabled", s.BellEnabled);
            s.CopyOnSelect = GetB(d, "copyOnSelect", s.CopyOnSelect);
            s.MultilinePasteWarning = GetB(d, "multilinePasteWarning", s.MultilinePasteWarning);
            s.NewTabInCurrentDirectory = GetB(d, "newTabInCurrentDirectory", s.NewTabInCurrentDirectory);
            s.LoadPowerShellProfile = GetB(d, "loadPowerShellProfile", s.LoadPowerShellProfile);

            s.ConfirmCloseRunningTab = GetB(d, "confirmCloseRunningTab", s.ConfirmCloseRunningTab);
            s.NewTabWhenLastClosed = GetB(d, "newTabWhenLastClosed", s.NewTabWhenLastClosed);
            s.RestoreWindowBounds = GetB(d, "restoreWindowBounds", s.RestoreWindowBounds);
            s.StartMaximized = GetB(d, "startMaximized", s.StartMaximized);
            s.StartFullscreen = GetB(d, "startFullscreen", s.StartFullscreen);
            s.WindowLeft = GetDNullable(d, "windowLeft");
            s.WindowTop = GetDNullable(d, "windowTop");
            s.WindowWidth = GetD(d, "windowWidth", s.WindowWidth);
            s.WindowHeight = GetD(d, "windowHeight", s.WindowHeight);
            s.WindowMaximized = GetB(d, "windowMaximized", s.WindowMaximized);

            s.DiagnosticLogging = GetB(d, "diagnosticLogging", s.DiagnosticLogging);
            s.LogVerbosity = GetS(d, "logVerbosity", s.LogVerbosity);

            s.Validate();
            return s;
        }

        private static ShellKind ParseShell(string s, ShellKind def)
        {
            try { return (ShellKind)Enum.Parse(typeof(ShellKind), s, true); }
            catch { return def; }
        }

        private static CursorStyle ParseCursor(string s, CursorStyle def)
        {
            try { return (CursorStyle)Enum.Parse(typeof(CursorStyle), s, true); }
            catch { return def; }
        }

        private static string GetS(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is string) return (string)v;
            return def;
        }

        private static bool GetB(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d.TryGetValue(k, out v))
            {
                if (v is bool) return (bool)v;
                if (v is double) return (double)v != 0;
            }
            return def;
        }

        private static double GetD(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null)
            {
                if (v is double) return (double)v;
                double parsed;
                if (double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
            }
            return def;
        }

        private static double GetDNullable(Dictionary<string, object> d, string k)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is double) return (double)v;
            return double.NaN;
        }

        private static int GetI(Dictionary<string, object> d, string k, int def)
        {
            return (int)Math.Round(GetD(d, k, def));
        }

        private static double ClampD(double v, double lo, double hi, double def)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return def;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static int ClampI(int v, int lo, int hi, int def)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
