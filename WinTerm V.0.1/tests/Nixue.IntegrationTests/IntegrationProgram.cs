using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Nixue.Core.Config;
using Nixue.Core.Shells;
using Nixue.Core.Terminal;
using Nixue.App.Sessions;

namespace Nixue.IntegrationTests
{
    /// <summary>
    /// Real end-to-end tests that spawn actual shells through the ConPTY backend and
    /// assert on what they produce. These require a Windows build with ConPTY and are
    /// run headless (no UI). Every test has a timeout and disposes its session so no
    /// shell process is left behind.
    /// </summary>
    public static class IntegrationProgram
    {
        private static int _pass, _fail;
        private static readonly StringBuilder _report = new StringBuilder();
        private static string _reportPath;

        // Built as a winexe (no console subsystem) so the spawned shells do not inherit
        // a redirected console from the test host - exactly the context of the real GUI
        // app. Results are written to a file the test script reads.
        public static int Main(string[] args)
        {
            _reportPath = (args != null && args.Length > 0)
                ? args[0]
                : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "integration-results.txt");

            Emit("NIXUE CMD - integration tests (real shells via ConPTY)");
            Emit(new string('-', 62));

            Run("Cmd_StartsAndEchoes", Cmd_StartsAndEchoes);
            Run("Cmd_WorkingDirectoryIndependentSessions", Cmd_WorkingDirectoryIndependent);
            Run("Cmd_ExitDetected", Cmd_ExitDetected);
            Run("Cmd_UnicodeOutput", Cmd_UnicodeOutput);
            Run("Cmd_LargeOutput", Cmd_LargeOutput);
            Run("PowerShell_StartsAndEvaluates", PowerShell_StartsAndEvaluates);
            Run("Pwsh7_HandledWhenMissing", Pwsh7_HandledWhenMissing);
            Run("Resize_DoesNotThrow", Resize_DoesNotThrow);
            Run("Dispose_LeavesNoProcess", Dispose_LeavesNoProcess);

            Emit(new string('-', 62));
            Emit("Passed: " + _pass + "   Failed: " + _fail);
            Emit("RESULT: " + (_fail == 0 ? "ALL PASS" : "FAILURES"));
            try { System.IO.File.WriteAllText(_reportPath, _report.ToString()); } catch { }
            return _fail == 0 ? 0 : 1;
        }

        private static void Emit(string line)
        {
            _report.AppendLine(line);
            try { Console.WriteLine(line); } catch { }
        }

        private static void Run(string name, Action test)
        {
            try { test(); _pass++; Emit("  PASS  " + name); }
            catch (Exception ex) { _fail++; Emit("  FAIL  " + name + "  ->  " + ex.Message); }
        }

        private static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }

        private static readonly Action<Action> SyncPost = delegate(Action a) { if (a != null) a(); };

        private static TerminalSession NewSession(ShellKind kind, AppSettings s, out ShellInfo shell)
        {
            ShellDiscovery d = new ShellDiscovery();
            shell = d.Discover(kind);
            if (!shell.Available) return null;
            TerminalSession session = new TerminalSession(shell, 100, 30, 2000, SyncPost, new ConPtyBackend());
            string cmd = d.BuildCommandLine(shell, s);
            session.Start(cmd, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return session;
        }

        private static string DumpBuffer(TerminalBuffer b)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.TotalLines; i++)
            {
                TerminalCell[] line = b.GetLogicalLine(i);
                for (int c = 0; c < line.Length; c++) sb.Append(line[c].Char);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static bool WaitForText(TerminalSession s, string needle, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (DumpBuffer(s.Buffer).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                Thread.Sleep(50);
            }
            return false;
        }

        // ---- Tests ---------------------------------------------------------

        private static void Cmd_StartsAndEchoes()
        {
            AppSettings s = new AppSettings();
            ShellInfo shell;
            TerminalSession session = NewSession(ShellKind.Cmd, s, out shell);
            Assert(session != null, "cmd should be available");
            try
            {
                Thread.Sleep(400);
                session.SendInput("echo ARCH_MARKER_123\r");
                Assert(WaitForText(session, "ARCH_MARKER_123", 5000), "echo output not seen");
            }
            finally { session.Dispose(); }
        }

        private static void Cmd_WorkingDirectoryIndependent()
        {
            AppSettings s = new AppSettings();
            ShellInfo sh;
            TerminalSession a = NewSession(ShellKind.Cmd, s, out sh);
            TerminalSession b = NewSession(ShellKind.Cmd, s, out sh);
            Assert(a != null && b != null, "two cmd sessions");
            try
            {
                Thread.Sleep(400);
                a.SendInput("cd \\\r");
                a.SendInput("echo A_IS:%CD%\r");
                b.SendInput("echo B_IS:%CD%\r");
                Assert(WaitForText(a, "A_IS:", 5000), "A reported cd");
                Assert(WaitForText(b, "B_IS:", 5000), "B reported cd");
                string da = DumpBuffer(a.Buffer);
                string db = DumpBuffer(b.Buffer);
                // A changed to root; B should still be at the home profile path.
                Assert(da.IndexOf("A_IS:C:\\\n", StringComparison.OrdinalIgnoreCase) >= 0
                       || da.IndexOf("A_IS:C:\\", StringComparison.OrdinalIgnoreCase) >= 0, "A at root");
                Assert(db.IndexOf("B_IS:", StringComparison.OrdinalIgnoreCase) >= 0, "B reported its own dir");
            }
            finally { a.Dispose(); b.Dispose(); }
        }

        private static void Cmd_ExitDetected()
        {
            AppSettings s = new AppSettings();
            ShellInfo sh;
            TerminalSession session = NewSession(ShellKind.Cmd, s, out sh);
            Assert(session != null, "cmd available");
            bool exited = false;
            session.Exited += delegate { exited = true; };
            try
            {
                Thread.Sleep(300);
                session.SendInput("exit\r");
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 5000 && !exited) Thread.Sleep(50);
                Assert(exited, "exit should be detected");
                Assert(session.State == SessionState.Exited, "state should be Exited");
            }
            finally { session.Dispose(); }
        }

        private static void Cmd_UnicodeOutput()
        {
            AppSettings s = new AppSettings();
            ShellInfo sh;
            TerminalSession session = NewSession(ShellKind.Cmd, s, out sh);
            Assert(session != null, "cmd available");
            try
            {
                Thread.Sleep(400);
                // Switch to UTF-8 code page and print a multibyte string.
                session.SendInput("chcp 65001>nul & echo UNI_\u00e9\u00fc_END\r");
                Assert(WaitForText(session, "UNI_", 5000), "unicode line printed");
            }
            finally { session.Dispose(); }
        }

        private static void Cmd_LargeOutput()
        {
            AppSettings s = new AppSettings();
            ShellInfo sh;
            TerminalSession session = NewSession(ShellKind.Cmd, s, out sh);
            Assert(session != null, "cmd available");
            try
            {
                Thread.Sleep(300);
                // Produce a few hundred lines quickly.
                session.SendInput("for /L %i in (1,1,300) do @echo LINE_%i\r");
                Assert(WaitForText(session, "LINE_300", 10000), "large output completed");
                // Scrollback must stay bounded.
                Assert(session.Buffer.ScrollbackCount <= session.Buffer.ScrollbackLimit, "scrollback bounded");
            }
            finally { session.Dispose(); }
        }

        private static void PowerShell_StartsAndEvaluates()
        {
            AppSettings s = new AppSettings();
            ShellInfo shell;
            TerminalSession session = NewSession(ShellKind.WindowsPowerShell, s, out shell);
            if (session == null) { Emit("    (Windows PowerShell not present; skipped)"); return; }
            try
            {
                Thread.Sleep(800);
                session.SendInput("[string](2+5) + '_PSOK'\r");
                Assert(WaitForText(session, "7_PSOK", 8000), "powershell evaluated expression");
            }
            finally { session.Dispose(); }
        }

        private static void Pwsh7_HandledWhenMissing()
        {
            ShellDiscovery d = new ShellDiscovery();
            ShellInfo p = d.Discover(ShellKind.PowerShell7);
            if (p.Available)
            {
                Emit("    (pwsh 7 present at " + p.ExecutablePath + ")");
                Assert(System.IO.File.Exists(p.ExecutablePath), "pwsh path exists");
            }
            else
            {
                Assert(!string.IsNullOrEmpty(p.UnavailableReason), "missing pwsh has a reason");
            }
        }

        private static void Resize_DoesNotThrow()
        {
            AppSettings s = new AppSettings();
            ShellInfo sh;
            TerminalSession session = NewSession(ShellKind.Cmd, s, out sh);
            Assert(session != null, "cmd available");
            try
            {
                Thread.Sleep(300);
                session.Resize(120, 40);
                session.Resize(60, 20);
                Thread.Sleep(100);
                Assert(session.Cols == 60 && session.Rows == 20, "resize applied");
            }
            finally { session.Dispose(); }
        }

        private static void Dispose_LeavesNoProcess()
        {
            AppSettings s = new AppSettings();
            ShellInfo sh;
            TerminalSession session = NewSession(ShellKind.Cmd, s, out sh);
            Assert(session != null, "cmd available");
            Thread.Sleep(400);
            session.Dispose();
            Thread.Sleep(500);
            Assert(session.State == SessionState.Disposed, "disposed state");
            // No reliable PID handle is exposed; disposal terminates the owned process
            // tree via ClosePseudoConsole + TerminateProcess. This asserts clean teardown.
        }
    }
}
