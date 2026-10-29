using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Nixue.WinFetch
{
    /// <summary>
    /// A neofetch-style system summary for Windows, shipped as the terminal's built-in
    /// `winfetch` command. It clears the screen, draws the Windows logo rendered in
    /// binary (1s and 0s) at the very top, and prints Windows-native system facts to
    /// its right - including LIVE CPU and GPU usage percentages sampled at run time.
    /// Depends only on the .NET Framework base libraries.
    /// </summary>
    public static class WinFetch
    {
        // Windows brand blue (#0078D7) and a lighter accent for labels.
        private const string Logo = "\x1b[38;2;0;120;215m";
        private const string Label = "\x1b[1;38;2;93;173;247m"; // accent (matches Nixue Dark)
        private const string Reset = "\x1b[0m";

        public static int Main(string[] args)
        {
            EnableVt();

            bool watch = false;
            foreach (string a in args)
                if (a == "--watch" || a == "-w") watch = true;

            if (watch)
            {
                Console.Write("\x1b[?25l"); // hide cursor
                try
                {
                    while (!Console.KeyAvailable)
                    {
                        Render();
                        Thread.Sleep(900);
                    }
                    Console.ReadKey(true);
                }
                finally { Console.Write("\x1b[?25h"); }
            }
            else
            {
                Render();
            }
            return 0;
        }

        private static void Render()
        {
            // Build the ENTIRE frame first (this is where the usage sampling happens),
            // then clear and print in a single write. This avoids leaving a blank,
            // cleared screen during the sampling window - the previous content stays
            // put until the finished frame is ready, so it appears instantly.
            string[] logo = BuildLogo();
            List<string> info = BuildInfo();

            int rows = Math.Max(logo.Length, info.Count);
            StringBuilder sb = new StringBuilder();
            sb.Append("\x1b[2J\x1b[H"); // clear + home
            for (int i = 0; i < rows; i++)
            {
                string l = i < logo.Length ? logo[i] : "";
                string padded = PadLogo(l);
                sb.Append(Logo).Append(padded).Append(Reset).Append("   ");
                if (i < info.Count) sb.Append(info[i]);
                sb.Append("\r\n");
            }
            Console.Write(sb.ToString());
            Console.Out.Flush();
        }

        // ---- Windows logo in binary ----------------------------------------

        private const int LogoWidth = 17;

        private static string[] BuildLogo()
        {
            // Four panes of the Windows flag, drawn with 1s and 0s (binary).
            return new string[]
            {
                "01010101 01010101",
                "10101010 10101010",
                "01010101 01010101",
                "10101010 10101010",
                "01010101 01010101",
                "                 ",
                "01010101 01010101",
                "10101010 10101010",
                "01010101 01010101",
                "10101010 10101010",
                "01010101 01010101",
            };
        }

        private static string PadLogo(string s)
        {
            if (s.Length >= LogoWidth) return s;
            return s + new string(' ', LogoWidth - s.Length);
        }

        // ---- Info lines ----------------------------------------------------

        private static List<string> BuildInfo()
        {
            List<string> lines = new List<string>();

            string user = Safe(delegate { return Environment.UserName; }, "user");
            string host = Safe(delegate { return Environment.MachineName; }, "pc");
            string title = Label + user + Reset + "@" + Label + host + Reset;
            lines.Add(title);
            lines.Add(new string('-', (user + "@" + host).Length));

            lines.Add(Row("OS", OsString()));
            lines.Add(Row("Host", HostModel()));
            lines.Add(Row("Winver", WinVer()));
            lines.Add(Row("Uptime", Uptime()));
            lines.Add(Row("Packages", PackageCount()));
            lines.Add(Row("Shell", ShellName()));
            lines.Add(Row("Resolution", Resolution()));
            lines.Add(Row("Terminal", "Nixue CMD"));

            // Live usage sampled over one short window shared by CPU and GPU.
            string cpuName = CpuName();
            string gpuName = GpuName();
            int cpuPct, gpuPct;
            SampleUsage(out cpuPct, out gpuPct);
            lines.Add(Row("CPU", cpuName + " | " + cpuPct + "%"));
            lines.Add(Row("GPU", gpuName + " | " + gpuPct + "%"));

            lines.Add(Row("Memory", Memory()));
            lines.Add("");
            lines.Add(ColorBlocks(false));
            lines.Add(ColorBlocks(true));
            return lines;
        }

        private static string Row(string label, string value)
        {
            return Label + label + Reset + ": " + value;
        }

        // ---- Data sources --------------------------------------------------

        private static string OsString()
        {
            string product = RegLM(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "Windows");
            int build = ParseInt(RegLM(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", "0"), 0);
            if (build >= 22000 && product.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                product = product.Replace("Windows 10", "Windows 11");
            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            return product + " " + arch;
        }

        private static string WinVer()
        {
            string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string build = RegLM(key, "CurrentBuildNumber", "0");
            string ubr = RegLM(key, "UBR", "");
            string dv = RegLM(key, "DisplayVersion", "");
            string v = "10.0." + build;
            if (!string.IsNullOrEmpty(ubr)) v += "." + ubr;
            if (!string.IsNullOrEmpty(dv)) v += " (" + dv + ")";
            return v;
        }

        private static string HostModel()
        {
            string man = RegLM(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer", "");
            string prod = RegLM(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", "");
            string combined = (man + " " + prod).Trim();
            return string.IsNullOrEmpty(combined) ? Environment.MachineName : combined;
        }

        private static string Uptime()
        {
            try
            {
                ulong ms = GetTickCount64();
                TimeSpan t = TimeSpan.FromMilliseconds(ms);
                StringBuilder sb = new StringBuilder();
                if (t.Days > 0) sb.Append(t.Days).Append(t.Days == 1 ? " day, " : " days, ");
                if (t.Hours > 0) sb.Append(t.Hours).Append(t.Hours == 1 ? " hour, " : " hours, ");
                sb.Append(t.Minutes).Append(t.Minutes == 1 ? " min" : " mins");
                return sb.ToString();
            }
            catch { return "?"; }
        }

        private static string PackageCount()
        {
            int n = 0;
            n += CountUninstall(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            n += CountUninstall(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            n += CountUninstall(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            return n + " (installed)";
        }

        private static int CountUninstall(RegistryHive hive, string path)
        {
            int count = 0;
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                using (RegistryKey k = root.OpenSubKey(path))
                {
                    if (k == null) return 0;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey e = k.OpenSubKey(sub))
                            {
                                if (e == null) continue;
                                object name = e.GetValue("DisplayName");
                                object sys = e.GetValue("SystemComponent");
                                if (name != null && (sys == null || Convert.ToInt32(sys) == 0)) count++;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return count;
        }

        private static string ShellName()
        {
            string s = Environment.GetEnvironmentVariable("NIXUE_SHELL");
            return string.IsNullOrEmpty(s) ? "Nixue CMD" : s;
        }

        private static string Resolution()
        {
            try
            {
                int w = GetSystemMetrics(0);
                int h = GetSystemMetrics(1);
                if (w > 0 && h > 0) return w + "x" + h;
            }
            catch { }
            return "?";
        }

        private static string CpuName()
        {
            string n = RegLM(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "");
            return string.IsNullOrEmpty(n) ? "CPU" : n.Trim();
        }

        private static string GpuName()
        {
            // Primary display adapter driver description.
            string n = RegLM(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000",
                             "DriverDesc", "");
            return string.IsNullOrEmpty(n) ? "GPU" : n.Trim();
        }

        private static string Memory()
        {
            try
            {
                MEMORYSTATUSEX m = new MEMORYSTATUSEX();
                m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref m))
                {
                    long totalMiB = (long)(m.ullTotalPhys / (1024 * 1024));
                    long usedMiB = (long)((m.ullTotalPhys - m.ullAvailPhys) / (1024 * 1024));
                    return usedMiB + "MiB / " + totalMiB + "MiB";
                }
            }
            catch { }
            return "?";
        }

        // ---- Live CPU / GPU usage ------------------------------------------

        // Live CPU and GPU usage via PDH (one query, wildcard GPU counter). This is far
        // faster than creating a PerformanceCounter per GPU-engine instance, which could
        // take several seconds while the system is busy.
        private static void SampleUsage(out int cpuPct, out int gpuPct)
        {
            cpuPct = 0;
            gpuPct = 0;
            IntPtr query = IntPtr.Zero;
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return;
                IntPtr cpuCtr, gpuCtr;
                bool haveCpu = PdhAddEnglishCounter(query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out cpuCtr) == 0;
                bool haveGpu = PdhAddEnglishCounter(query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out gpuCtr) == 0;

                PdhCollectQueryData(query);
                Thread.Sleep(400); // sampling window
                PdhCollectQueryData(query);

                if (haveCpu) cpuPct = Clamp((int)Math.Round(ReadSingle(cpuCtr)));
                if (haveGpu) gpuPct = Clamp((int)Math.Round(ReadSum(gpuCtr)));
            }
            catch { }
            finally
            {
                if (query != IntPtr.Zero) PdhCloseQuery(query);
            }
        }

        private static double ReadSingle(IntPtr counter)
        {
            try
            {
                uint type;
                PDH_FMT_COUNTERVALUE_DOUBLE val;
                if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out type, out val) == 0 && val.CStatus == 0)
                    return val.doubleValue;
            }
            catch { }
            return 0;
        }

        private static double ReadSum(IntPtr counter)
        {
            try
            {
                uint size = 0, count = 0;
                uint r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
                if (r != PDH_MORE_DATA || size == 0) return 0;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buf);
                    if (r != 0) return 0;
                    double sum = 0;
                    int itemSize = Marshal.SizeOf(typeof(PDH_FMT_COUNTERVALUE_ITEM_DOUBLE));
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr p = (IntPtr)(buf.ToInt64() + (long)i * itemSize);
                        PDH_FMT_COUNTERVALUE_ITEM_DOUBLE item =
                            (PDH_FMT_COUNTERVALUE_ITEM_DOUBLE)Marshal.PtrToStructure(p, typeof(PDH_FMT_COUNTERVALUE_ITEM_DOUBLE));
                        if (item.FmtValue.CStatus == 0) sum += item.FmtValue.doubleValue;
                    }
                    return sum;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return 0; }
        }

        private static int Clamp(int v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }

        // ---- Colour swatches ----------------------------------------------

        private static string ColorBlocks(bool bright)
        {
            StringBuilder sb = new StringBuilder();
            int start = bright ? 8 : 0;
            for (int i = 0; i < 8; i++)
            {
                int code = (start + i);
                string fg = code < 8 ? ("\x1b[4" + code + "m") : ("\x1b[10" + (code - 8) + "m");
                sb.Append(fg).Append("   ");
            }
            sb.Append(Reset);
            return sb.ToString();
        }

        // ---- Helpers -------------------------------------------------------

        private static string RegLM(string path, string name, string def)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                using (RegistryKey k = root.OpenSubKey(path))
                {
                    if (k == null) return def;
                    object v = k.GetValue(name);
                    return v == null ? def : v.ToString();
                }
            }
            catch { return def; }
        }

        private static int ParseInt(string s, int def)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static string Safe(Func<string> f, string def)
        {
            try { string s = f(); return string.IsNullOrEmpty(s) ? def : s; }
            catch { return def; }
        }

        // ---- Native --------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        // ---- PDH (performance data helper) for live CPU / GPU usage ----------

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE_DOUBLE
        {
            public uint CStatus;
            public double doubleValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE_ITEM_DOUBLE
        {
            public IntPtr szName;
            public PDH_FMT_COUNTERVALUE_DOUBLE FmtValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE_DOUBLE pValue);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr ItemBuffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr hQuery);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr h, out uint mode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr h, uint mode);

        private static void EnableVt()
        {
            try
            {
                IntPtr h = GetStdHandle(-11); // STD_OUTPUT_HANDLE
                uint mode;
                if (GetConsoleMode(h, out mode))
                    SetConsoleMode(h, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
            }
            catch { }
        }
    }
}
