using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nixue.Core.Config;

namespace Nixue.Core.Shells
{
    /// <summary>
    /// Discovers the shells available on this machine and builds the command line used
    /// to launch each one. Discovery prefers well-known system locations over a bare
    /// PATH lookup so the app never launches an arbitrary <c>cmd.exe</c> planted in the
    /// working directory.
    /// </summary>
    public sealed class ShellDiscovery
    {
        public List<ShellInfo> DiscoverAll()
        {
            List<ShellInfo> list = new List<ShellInfo>();
            list.Add(DiscoverCmd());
            list.Add(DiscoverWindowsPowerShell());
            list.Add(DiscoverPowerShell7());
            return list;
        }

        public ShellInfo Discover(ShellKind kind)
        {
            switch (kind)
            {
                case ShellKind.WindowsPowerShell: return DiscoverWindowsPowerShell();
                case ShellKind.PowerShell7: return DiscoverPowerShell7();
                default: return DiscoverCmd();
            }
        }

        private static string SystemDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.System); }
        }

        private ShellInfo DiscoverCmd()
        {
            ShellInfo info = new ShellInfo(ShellKind.Cmd, "CMD");
            // Prefer the system directory; fall back to COMSPEC only if it is under System32.
            string sysCmd = Path.Combine(SystemDir, "cmd.exe");
            if (File.Exists(sysCmd))
            {
                info.ExecutablePath = sysCmd;
                info.Available = true;
            }
            else
            {
                string comspec = Environment.GetEnvironmentVariable("COMSPEC");
                if (!string.IsNullOrEmpty(comspec) && File.Exists(comspec))
                {
                    info.ExecutablePath = comspec;
                    info.Available = true;
                }
                else
                {
                    info.UnavailableReason = "cmd.exe was not found in the system directory.";
                }
            }
            if (info.Available)
            {
                info.Version = GetWindowsVersionString();
            }
            return info;
        }

        private ShellInfo DiscoverWindowsPowerShell()
        {
            ShellInfo info = new ShellInfo(ShellKind.WindowsPowerShell, "Windows PowerShell");
            string path = Path.Combine(SystemDir, "WindowsPowerShell");
            path = Path.Combine(path, "v1.0");
            path = Path.Combine(path, "powershell.exe");
            if (File.Exists(path))
            {
                info.ExecutablePath = path;
                info.Available = true;
            }
            else
            {
                info.UnavailableReason = "Windows PowerShell (powershell.exe) was not found.";
            }
            return info;
        }

        private ShellInfo DiscoverPowerShell7()
        {
            ShellInfo info = new ShellInfo(ShellKind.PowerShell7, "PowerShell 7");
            string found = FindPwsh();
            if (!string.IsNullOrEmpty(found))
            {
                info.ExecutablePath = found;
                info.Available = true;
            }
            else
            {
                info.UnavailableReason = "PowerShell 7 (pwsh.exe) is not installed. Install it to enable this option.";
            }
            return info;
        }

        private string FindPwsh()
        {
            List<string> candidates = new List<string>();

            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            AddPwshCandidates(candidates, pf);
            AddPwshCandidates(candidates, pf86);

            // winget-installed per-user copies.
            if (!string.IsNullOrEmpty(localApp))
            {
                candidates.Add(Path.Combine(localApp, @"Microsoft\WindowsApps\pwsh.exe"));
            }

            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
            }

            // Last resort: search PATH, but validate the file exists.
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] dirs = pathEnv.Split(';');
                foreach (string d in dirs)
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    string candidate;
                    try { candidate = Path.Combine(d.Trim(), "pwsh.exe"); }
                    catch { continue; }
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        private void AddPwshCandidates(List<string> list, string programFiles)
        {
            if (string.IsNullOrEmpty(programFiles)) return;
            string root = Path.Combine(programFiles, "PowerShell");
            if (!Directory.Exists(root)) return;
            // Prefer the highest major version directory (7, 8, ...).
            try
            {
                string[] versionDirs = Directory.GetDirectories(root);
                // Sort descending so a newer major version wins.
                Array.Sort(versionDirs, StringComparer.OrdinalIgnoreCase);
                Array.Reverse(versionDirs);
                foreach (string vd in versionDirs)
                {
                    list.Add(Path.Combine(vd, "pwsh.exe"));
                }
            }
            catch
            {
                // ignore and fall through
            }
        }

        private static string GetWindowsVersionString()
        {
            try { return "Windows " + Environment.OSVersion.Version.ToString(); }
            catch { return ""; }
        }

        // ---- Command-line construction -------------------------------------

        /// <summary>
        /// Builds the command line to launch <paramref name="shell"/>, applying the
        /// app-owned minimal prompt and profile policy. The customization is scoped to
        /// this session only; no global profile or environment is modified.
        /// </summary>
        public string BuildCommandLine(ShellInfo shell, AppSettings settings)
        {
            if (shell == null || !shell.Available) return null;
            string exe = Quote(shell.ExecutablePath);

            switch (shell.Kind)
            {
                case ShellKind.Cmd:
                    return BuildCmdCommandLine(exe, settings);
                case ShellKind.WindowsPowerShell:
                case ShellKind.PowerShell7:
                    return BuildPowerShellCommandLine(exe, settings);
                default:
                    return exe;
            }
        }

        private string BuildCmdCommandLine(string exe, AppSettings settings)
        {
            // /K runs an init string then stays interactive. The changes are local to
            // this cmd process only: we prepend our own bin directory (so `winfetch`
            // resolves), set session markers, and set the prompt.
            StringBuilder init = new StringBuilder();
            init.Append("set PATH=").Append(BinDir()).Append(";%PATH%");
            init.Append(" & set NIXUE=1");
            init.Append(" & set NIXUE_SHELL=CMD");
            if (settings.CustomPromptEnabled)
            {
                string tmpl = PromptToCmdTemplate(settings.PromptSymbol);
                // $P$S => current path then a space, placed to the LEFT of the prompt.
                if (settings.ShowPathInPrompt) tmpl = "$P$S" + tmpl;
                init.Append(" & prompt ").Append(tmpl);
            }
            return exe + " /K \"" + init.ToString() + "\"";
        }

        private string BuildPowerShellCommandLine(string exe, AppSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(exe);
            sb.Append(" -NoLogo");
            if (!settings.LoadPowerShellProfile) sb.Append(" -NoProfile");
            sb.Append(" -NoExit");

            // Init command uses only single-quoted strings internally so it can live
            // inside the outer double-quoted -Command argument without escaping.
            StringBuilder cmd = new StringBuilder();
            cmd.Append("$env:PATH='").Append(BinDir().Replace("'", "''")).Append(";'+$env:PATH;");
            cmd.Append(" $env:NIXUE='1'; $env:NIXUE_SHELL='PowerShell';");
            if (settings.CustomPromptEnabled)
            {
                string sym = SanitizePromptForPowerShell(settings.PromptSymbol);
                if (settings.ShowPathInPrompt)
                    cmd.Append(" function prompt { ($PWD.Path + ' ").Append(sym).Append(" ') }");
                else
                    cmd.Append(" function prompt { '").Append(sym).Append(" ' }");
            }
            sb.Append(" -Command \"").Append(cmd.ToString()).Append("\"");
            return sb.ToString();
        }

        /// <summary>
        /// The directory the running application lives in, which also holds helper
        /// commands such as winfetch.exe. Prepended to each session's PATH.
        /// </summary>
        private static string BinDir()
        {
            try
            {
                string b = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(b)) return b.TrimEnd('\\');
            }
            catch { }
            return ".";
        }

        /// <summary>Converts a prompt symbol to a cmd PROMPT template (ends with a space).</summary>
        public static string PromptToCmdTemplate(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) symbol = ">";
            StringBuilder sb = new StringBuilder();
            foreach (char c in symbol)
            {
                switch (c)
                {
                    case '>': sb.Append("$G"); break;
                    case '<': sb.Append("$L"); break;
                    case '|': sb.Append("$B"); break;
                    case '&': sb.Append("$A"); break;
                    case '=': sb.Append("$Q"); break;
                    case '$': sb.Append("$$"); break;
                    case ' ': sb.Append("$S"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append("$S"); // trailing space
            return sb.ToString();
        }

        /// <summary>Strips characters that would break the single-quoted PowerShell string.</summary>
        public static string SanitizePromptForPowerShell(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return ">";
            StringBuilder sb = new StringBuilder();
            foreach (char c in symbol)
            {
                if (c == '\'' || c == '"' || c == '`' || c < 0x20) continue;
                sb.Append(c);
            }
            string result = sb.ToString();
            return result.Length == 0 ? ">" : result;
        }

        private static string Quote(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.IndexOf(' ') >= 0 && !path.StartsWith("\""))
                return "\"" + path + "\"";
            return path;
        }
    }
}
