using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nixue.Core.Config;
using Nixue.Core.Diagnostics;
using Nixue.Core.Shells;
using Nixue.Core.Terminal;

namespace Nixue.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            TestRunner r = new TestRunner();

            // ---- JSON ------------------------------------------------------
            r.Add("Json_RoundTrip", Json_RoundTrip);
            r.Add("Json_ParseInvalidFails", Json_ParseInvalidFails);
            r.Add("Json_NestedAndEscapes", Json_NestedAndEscapes);

            // ---- Settings --------------------------------------------------
            r.Add("Settings_Defaults", Settings_Defaults);
            r.Add("Settings_RoundTrip", Settings_RoundTrip);
            r.Add("Settings_InvalidClamps", Settings_InvalidClamps);
            r.Add("Settings_MissingKeysUseDefaults", Settings_MissingKeysUseDefaults);
            r.Add("SettingsStore_SaveLoad", SettingsStore_SaveLoad);
            r.Add("SettingsStore_CorruptRecovers", SettingsStore_CorruptRecovers);

            // ---- Themes ----------------------------------------------------
            r.Add("Theme_BuiltIns", Theme_BuiltIns);
            r.Add("Theme_FromJsonFallback", Theme_FromJsonFallback);

            // ---- Shell discovery -------------------------------------------
            r.Add("Shell_CmdAvailable", Shell_CmdAvailable);
            r.Add("Shell_CmdPromptCommandLine", Shell_CmdPromptCommandLine);
            r.Add("Shell_PowerShellCommandLine", Shell_PowerShellCommandLine);
            r.Add("Shell_PromptTemplates", Shell_PromptTemplates);

            // ---- VT parser / emulator --------------------------------------
            r.Add("Vt_PrintBasic", Vt_PrintBasic);
            r.Add("Vt_CRLF", Vt_CRLF);
            r.Add("Vt_Backspace", Vt_Backspace);
            r.Add("Vt_CursorPosition", Vt_CursorPosition);
            r.Add("Vt_SgrSetsColors", Vt_SgrSetsColors);
            r.Add("Vt_SgrTrueColor", Vt_SgrTrueColor);
            r.Add("Vt_EraseLine", Vt_EraseLine);
            r.Add("Vt_SplitSequence", Vt_SplitSequence);
            r.Add("Vt_AutowrapDeferred", Vt_AutowrapDeferred);
            r.Add("Vt_AltScreen", Vt_AltScreen);
            r.Add("Vt_ScrollbackGrows", Vt_ScrollbackGrows);
            r.Add("Vt_DsrResponse", Vt_DsrResponse);
            r.Add("Vt_OscTitle", Vt_OscTitle);
            r.Add("Vt_TabStops", Vt_TabStops);

            // ---- Buffer ----------------------------------------------------
            r.Add("Buffer_ResizeGrowShrink", Buffer_ResizeGrowShrink);
            r.Add("Buffer_InsertDeleteChars", Buffer_InsertDeleteChars);
            r.Add("Buffer_InsertDeleteLines", Buffer_InsertDeleteLines);
            r.Add("Buffer_ScrollRegion", Buffer_ScrollRegion);
            r.Add("Buffer_ScrollbackLimit", Buffer_ScrollbackLimit);

            // ---- UTF-8 boundary (decoder approach used by the session) -----
            r.Add("Utf8_SplitMultibyte", Utf8_SplitMultibyte);

            // ---- Compatibility ---------------------------------------------
            r.Add("Compatibility_Detect", Compatibility_Detect);

            int code = r.Run();
            return code;
        }

        // ---- Helpers -------------------------------------------------------

        private static TerminalEmulator NewEmu(int rows, int cols)
        {
            TerminalBuffer buf = new TerminalBuffer(rows, cols, 1000);
            return new TerminalEmulator(buf);
        }

        private static string RowText(TerminalBuffer b, int row)
        {
            TerminalCell[] line = b.GetScreenRow(row);
            StringBuilder sb = new StringBuilder();
            for (int c = 0; c < line.Length; c++) sb.Append(line[c].Char);
            return sb.ToString().TrimEnd(' ', '\0');
        }

        // ---- JSON tests ----------------------------------------------------

        private static void Json_RoundTrip()
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["a"] = "hello";
            obj["b"] = 42.0;
            obj["c"] = true;
            obj["d"] = null;
            List<object> list = new List<object>();
            list.Add("x"); list.Add(1.0);
            obj["e"] = list;

            string s = Json.Serialize(obj, false);
            object back = Json.Parse(s);
            Dictionary<string, object> d = back as Dictionary<string, object>;
            Assert.NotNull(d, "parsed object");
            Assert.EqualStr("hello", (string)d["a"], "string value");
            Assert.Equal(42.0, d["b"], "number value");
            Assert.Equal(true, d["c"], "bool value");
            Assert.True(d["d"] == null, "null value");
            Assert.True(d["e"] is List<object>, "array value");
        }

        private static void Json_ParseInvalidFails()
        {
            object result;
            Assert.False(Json.TryParse("{ bad json", out result), "malformed object");
            Assert.False(Json.TryParse("[1,2,", out result), "truncated array");
            Assert.False(Json.TryParse("{\"a\":}", out result), "missing value");
            Assert.True(Json.TryParse("{\"a\":1}", out result), "valid object");
        }

        private static void Json_NestedAndEscapes()
        {
            string json = "{\"name\":\"a\\\"b\\n\",\"nested\":{\"k\":[true,false,null,3.5]}}";
            Dictionary<string, object> d = (Dictionary<string, object>)Json.Parse(json);
            Assert.EqualStr("a\"b\n", (string)d["name"], "escaped string");
            Dictionary<string, object> nested = (Dictionary<string, object>)d["nested"];
            List<object> arr = (List<object>)nested["k"];
            Assert.EqualInt(4, arr.Count, "array length");
            Assert.Equal(3.5, arr[3], "float element");
        }

        // ---- Settings tests ------------------------------------------------

        private static void Settings_Defaults()
        {
            AppSettings s = new AppSettings();
            Assert.Equal(ShellKind.Cmd, s.DefaultShell, "default shell is cmd");
            Assert.EqualStr(">", s.PromptSymbol, "default prompt symbol");
            Assert.Equal(CursorStyle.Block, s.CursorStyle, "default cursor block");
            Assert.EqualStr("Nixue Dark", s.ThemeName, "default theme");
            Assert.True(s.CursorBlink, "cursor blinks by default");
        }

        private static void Settings_RoundTrip()
        {
            AppSettings s = new AppSettings();
            s.TerminalFontSize = 18;
            s.DefaultShell = ShellKind.PowerShell7;
            s.CursorStyle = CursorStyle.Beam;
            s.ScrollbackLimit = 12345;
            AppSettings back = AppSettings.FromJson(s.ToJson());
            Assert.Equal(18.0, back.TerminalFontSize, "font size kept");
            Assert.Equal(ShellKind.PowerShell7, back.DefaultShell, "shell kept");
            Assert.Equal(CursorStyle.Beam, back.CursorStyle, "cursor kept");
            Assert.EqualInt(12345, back.ScrollbackLimit, "scrollback kept");
        }

        private static void Settings_InvalidClamps()
        {
            AppSettings s = new AppSettings();
            s.TerminalFontSize = 9999;
            s.WindowOpacity = 5.0;
            s.CursorBlinkRateMs = 1;
            s.Validate();
            Assert.True(s.TerminalFontSize <= 72, "font size clamped");
            Assert.True(s.WindowOpacity <= 1.0, "opacity clamped");
            Assert.True(s.CursorBlinkRateMs >= 100, "blink rate clamped");
        }

        private static void Settings_MissingKeysUseDefaults()
        {
            Dictionary<string, object> partial = new Dictionary<string, object>();
            partial["terminalFontSize"] = 20.0;
            AppSettings s = AppSettings.FromJson(partial);
            Assert.Equal(20.0, s.TerminalFontSize, "provided key read");
            Assert.EqualStr(">", s.PromptSymbol, "missing key defaulted");
            Assert.Equal(ShellKind.Cmd, s.DefaultShell, "missing enum defaulted");
        }

        private static void SettingsStore_SaveLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "archterm-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string file = Path.Combine(dir, "settings.json");
                SettingsStore store = new SettingsStore(file);
                AppSettings s = new AppSettings();
                s.ThemeName = "Midnight";
                s.TerminalFontSize = 16;
                Assert.True(store.Save(s), "save succeeded");
                Assert.True(File.Exists(file), "file written");
                AppSettings loaded = store.Load();
                Assert.EqualStr("Midnight", loaded.ThemeName, "theme persisted");
                Assert.Equal(16.0, loaded.TerminalFontSize, "size persisted");
            }
            finally { SafeDelete(dir); }
        }

        private static void SettingsStore_CorruptRecovers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "archterm-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "settings.json");
                File.WriteAllText(file, "this is not json {{{");
                SettingsStore store = new SettingsStore(file);
                AppSettings loaded = store.Load();
                Assert.NotNull(loaded, "recovered settings object");
                Assert.EqualStr("Nixue Dark", loaded.ThemeName, "fell back to defaults");
            }
            finally { SafeDelete(dir); }
        }

        // ---- Theme tests ---------------------------------------------------

        private static void Theme_BuiltIns()
        {
            List<Theme> all = ThemeLibrary.BuiltIn();
            Assert.EqualInt(4, all.Count, "four built-in themes");
            Assert.EqualStr("Nixue Dark", ThemeLibrary.Default().Name, "default is Nixue Dark");
            Theme hc = ThemeLibrary.GetByName("High Contrast");
            Assert.EqualStr("High Contrast", hc.Name, "lookup by name");
            Assert.EqualInt(16, hc.Ansi.Length, "16 ANSI colors");
        }

        private static void Theme_FromJsonFallback()
        {
            Theme def = ThemeLibrary.Default();
            Theme t = Theme.FromJson(null, def);
            Assert.EqualStr(def.Accent, t.Accent, "null json falls back");
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["accent"] = "#FF0000";
            Theme t2 = Theme.FromJson(d, def);
            Assert.EqualStr("#FF0000", t2.Accent, "override applied");
            Assert.EqualStr(def.Foreground, t2.Foreground, "unspecified kept");
        }

        // ---- Shell tests ---------------------------------------------------

        private static void Shell_CmdAvailable()
        {
            ShellInfo cmd = new ShellDiscovery().Discover(ShellKind.Cmd);
            Assert.True(cmd.Available, "cmd.exe found");
            Assert.True(File.Exists(cmd.ExecutablePath), "cmd path exists");
            Assert.True(cmd.ExecutablePath.ToLowerInvariant().EndsWith("cmd.exe"), "is cmd.exe");
        }

        private static void Shell_CmdPromptCommandLine()
        {
            AppSettings s = new AppSettings();
            s.CustomPromptEnabled = true;
            s.PromptSymbol = ">";
            ShellDiscovery d = new ShellDiscovery();
            ShellInfo cmd = d.Discover(ShellKind.Cmd);
            string cl = d.BuildCommandLine(cmd, s);
            Assert.True(cl.Contains("/K"), "uses /K");
            Assert.True(cl.Contains("prompt"), "sets prompt");
            Assert.True(cl.Contains("$G"), "maps > to $G");
            Assert.True(cl.Contains("$P"), "shows path in prompt");
            Assert.True(cl.Contains("set PATH="), "prepends bin dir to PATH");
        }

        private static void Shell_PowerShellCommandLine()
        {
            AppSettings s = new AppSettings();
            s.CustomPromptEnabled = true;
            s.LoadPowerShellProfile = false;
            ShellDiscovery d = new ShellDiscovery();
            ShellInfo ps = d.Discover(ShellKind.WindowsPowerShell);
            if (!ps.Available) return; // environment without WinPS: nothing to assert
            string cl = d.BuildCommandLine(ps, s);
            Assert.True(cl.Contains("-NoProfile"), "no profile when disabled");
            Assert.True(cl.Contains("function prompt"), "defines prompt function");
        }

        private static void Shell_PromptTemplates()
        {
            Assert.EqualStr("$G$S", ShellDiscovery.PromptToCmdTemplate(">"), "> maps to $G plus space");
            Assert.EqualStr("#$S", ShellDiscovery.PromptToCmdTemplate("#"), "# literal plus space");
            Assert.EqualStr(">", ShellDiscovery.SanitizePromptForPowerShell(">"), "ps keeps >");
            Assert.EqualStr(">", ShellDiscovery.SanitizePromptForPowerShell("'\">"), "ps strips quotes");
        }

        // ---- VT tests ------------------------------------------------------

        private static void Vt_PrintBasic()
        {
            TerminalEmulator e = NewEmu(5, 20);
            e.Feed("Hello World");
            Assert.EqualStr("Hello World", RowText(e.Buffer, 0), "printed text");
            Assert.EqualInt(11, e.Buffer.CursorX, "cursor advanced");
        }

        private static void Vt_CRLF()
        {
            TerminalEmulator e = NewEmu(5, 20);
            e.Feed("abc\r\ndef");
            Assert.EqualStr("abc", RowText(e.Buffer, 0), "first line");
            Assert.EqualStr("def", RowText(e.Buffer, 1), "second line");
            Assert.EqualInt(1, e.Buffer.CursorY, "cursor on row 1");
        }

        private static void Vt_Backspace()
        {
            TerminalEmulator e = NewEmu(5, 20);
            e.Feed("abc");
            e.Feed("\b");
            e.Feed("X");
            Assert.EqualStr("abX", RowText(e.Buffer, 0), "backspace overwrote");
        }

        private static void Vt_CursorPosition()
        {
            TerminalEmulator e = NewEmu(10, 40);
            e.Feed("\x1b[5;10H"); // row 5 col 10 (1-based)
            Assert.EqualInt(4, e.Buffer.CursorY, "row 0-based");
            Assert.EqualInt(9, e.Buffer.CursorX, "col 0-based");
            e.Feed("\x1b[3A"); // up 3
            Assert.EqualInt(1, e.Buffer.CursorY, "moved up");
        }

        private static void Vt_SgrSetsColors()
        {
            TerminalEmulator e = NewEmu(3, 10);
            e.Feed("\x1b[31mR\x1b[0mN");
            TerminalCell[] row = e.Buffer.GetScreenRow(0);
            Assert.Equal(ColorKind.Indexed, row[0].Fg.Kind, "red is indexed");
            Assert.EqualInt(1, row[0].Fg.Index, "index 1 (red)");
            Assert.Equal(ColorKind.Default, row[1].Fg.Kind, "reset to default");
        }

        private static void Vt_SgrTrueColor()
        {
            TerminalEmulator e = NewEmu(3, 10);
            e.Feed("\x1b[38;2;10;20;30mX");
            TerminalCell[] row = e.Buffer.GetScreenRow(0);
            Assert.Equal(ColorKind.Rgb, row[0].Fg.Kind, "truecolor kind");
            Assert.EqualInt(10, row[0].Fg.R, "R");
            Assert.EqualInt(20, row[0].Fg.G, "G");
            Assert.EqualInt(30, row[0].Fg.B, "B");
        }

        private static void Vt_EraseLine()
        {
            TerminalEmulator e = NewEmu(3, 10);
            e.Feed("abcdef");
            e.Feed("\x1b[1;4H"); // col 4 (0-based 3)
            e.Feed("\x1b[0K");   // erase from cursor to end
            Assert.EqualStr("abc", RowText(e.Buffer, 0), "erased to end of line");
        }

        private static void Vt_SplitSequence()
        {
            TerminalEmulator e = NewEmu(5, 20);
            // Split "\x1b[31m" across three feeds, then text.
            e.Feed("\x1b[3");
            e.Feed("1");
            e.Feed("mZ");
            TerminalCell[] row = e.Buffer.GetScreenRow(0);
            Assert.EqualInt(1, row[0].Fg.Index, "split SGR still applied");
            Assert.Equal('Z', row[0].Char, "char after split sequence");
        }

        private static void Vt_AutowrapDeferred()
        {
            TerminalEmulator e = NewEmu(3, 4);
            e.Feed("ABCD");    // fills row 0; wrap is deferred
            Assert.EqualInt(0, e.Buffer.CursorY, "no wrap yet after last col");
            e.Feed("E");       // now wraps
            Assert.EqualInt(1, e.Buffer.CursorY, "wrapped to next row");
            Assert.EqualStr("ABCD", RowText(e.Buffer, 0), "row 0 full");
            Assert.EqualStr("E", RowText(e.Buffer, 1), "row 1 has E");
        }

        private static void Vt_AltScreen()
        {
            TerminalEmulator e = NewEmu(4, 10);
            e.Feed("primary");
            e.Feed("\x1b[?1049h"); // enter alt
            Assert.True(e.Buffer.AltScreenActive, "alt active");
            Assert.EqualStr("", RowText(e.Buffer, 0), "alt screen cleared");
            e.Feed("\x1b[?1049l"); // leave alt
            Assert.False(e.Buffer.AltScreenActive, "back to primary");
            Assert.EqualStr("primary", RowText(e.Buffer, 0), "primary restored");
        }

        private static void Vt_ScrollbackGrows()
        {
            TerminalEmulator e = NewEmu(3, 10);
            for (int i = 0; i < 10; i++) e.Feed("line" + i + "\r\n");
            Assert.True(e.Buffer.ScrollbackCount > 0, "scrollback accumulated");
            Assert.True(e.Buffer.TotalLines >= 10, "total lines grew");
        }

        private static void Vt_DsrResponse()
        {
            TerminalEmulator e = NewEmu(10, 40);
            string reply = null;
            e.ResponseReady += delegate(string s) { reply = s; };
            e.Feed("\x1b[5;7H"); // move cursor
            e.Feed("\x1b[6n");   // request cursor position
            Assert.NotNull(reply, "got a reply");
            Assert.EqualStr("\x1b[5;7R", reply, "CPR row;col");
        }

        private static void Vt_OscTitle()
        {
            TerminalEmulator e = NewEmu(3, 10);
            string title = null;
            e.TitleChanged += delegate(string s) { title = s; };
            e.Feed("\x1b]0;My Title\x07");
            Assert.EqualStr("My Title", title, "OSC title parsed");
        }

        private static void Vt_TabStops()
        {
            TerminalEmulator e = NewEmu(3, 40);
            e.Feed("a\tb");
            Assert.EqualInt(9, e.Buffer.CursorX, "tab to column 8 then b");
            TerminalCell[] row = e.Buffer.GetScreenRow(0);
            Assert.Equal('a', row[0].Char, "a at col 0");
            Assert.Equal('b', row[8].Char, "b at col 8");
        }

        // ---- Buffer tests --------------------------------------------------

        private static void Buffer_ResizeGrowShrink()
        {
            TerminalBuffer b = new TerminalBuffer(5, 10, 100);
            b.Print('X', CellFlags.None);
            b.Resize(8, 20);
            Assert.EqualInt(8, b.Rows, "rows grew");
            Assert.EqualInt(20, b.Cols, "cols grew");
            b.Resize(3, 5);
            Assert.EqualInt(3, b.Rows, "rows shrank");
            Assert.EqualInt(5, b.Cols, "cols shrank");
            Assert.True(b.CursorX < b.Cols && b.CursorY < b.Rows, "cursor in bounds");
        }

        private static void Buffer_InsertDeleteChars()
        {
            TerminalBuffer b = new TerminalBuffer(3, 10, 100);
            Feed(b, "abcdef");
            b.SetCursorAbsolute(0, 0);
            b.InsertChars(2);
            Assert.Equal(' ', b.GetScreenRow(0)[0].Char, "inserted blank");
            Assert.Equal('a', b.GetScreenRow(0)[2].Char, "shifted right");
            b.SetCursorAbsolute(0, 0);
            b.DeleteChars(2);
            Assert.Equal('a', b.GetScreenRow(0)[0].Char, "deleted back");
        }

        private static void Buffer_InsertDeleteLines()
        {
            TerminalBuffer b = new TerminalBuffer(4, 6, 100);
            Feed(b, "AAAAAA"); b.CarriageReturn(); b.LineFeed();
            Feed(b, "BBBBBB"); b.CarriageReturn(); b.LineFeed();
            b.SetCursorAbsolute(0, 0);
            b.InsertLines(1);
            Assert.Equal(' ', b.GetScreenRow(0)[0].Char, "blank line inserted");
            Assert.Equal('A', b.GetScreenRow(1)[0].Char, "A moved down");
            b.SetCursorAbsolute(0, 0);
            b.DeleteLines(1);
            Assert.Equal('A', b.GetScreenRow(0)[0].Char, "A back to top");
        }

        private static void Buffer_ScrollRegion()
        {
            TerminalBuffer b = new TerminalBuffer(5, 5, 100);
            b.SetScrollRegion(1, 3); // rows 1..3
            Assert.EqualInt(1, b.ScrollTop, "top set");
            Assert.EqualInt(3, b.ScrollBottom, "bottom set");
            b.ResetScrollRegion();
            Assert.EqualInt(0, b.ScrollTop, "reset top");
            Assert.EqualInt(4, b.ScrollBottom, "reset bottom");
        }

        private static void Buffer_ScrollbackLimit()
        {
            TerminalBuffer b = new TerminalBuffer(3, 5, 4); // small limit
            for (int i = 0; i < 50; i++) { Feed(b, "x"); b.CarriageReturn(); b.LineFeed(); }
            Assert.True(b.ScrollbackCount <= 4, "scrollback bounded to limit");
        }

        private static void Feed(TerminalBuffer b, string s)
        {
            foreach (char c in s) b.Print(c, CellFlags.None);
        }

        // ---- UTF-8 boundary ------------------------------------------------

        private static void Utf8_SplitMultibyte()
        {
            // '€' = E2 82 AC. Split across two reads; a stateful Decoder must join them.
            byte[] all = Encoding.UTF8.GetBytes("a€b");
            Decoder dec = Encoding.UTF8.GetDecoder();
            StringBuilder sb = new StringBuilder();
            // Feed byte-by-byte to stress partial sequences.
            char[] outBuf = new char[8];
            for (int i = 0; i < all.Length; i++)
            {
                int n = dec.GetChars(all, i, 1, outBuf, 0, false);
                sb.Append(outBuf, 0, n);
            }
            Assert.EqualStr("a€b", sb.ToString(), "decoder joins split multibyte char");
        }

        // ---- Compatibility -------------------------------------------------

        private static void Compatibility_Detect()
        {
            CompatibilityInfo c = CompatibilityInfo.Detect();
            Assert.NotNull(c.OsVersion, "os version");
            Assert.NotNull(c.Shells, "shells list");
            string report = c.BuildReport();
            Assert.True(report.Contains("NIXUE CMD"), "report has header");
        }

        private static void SafeDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
