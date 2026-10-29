using System;
using System.Collections.Generic;
using System.Text;
using Nixue.Core.Shells;

namespace Nixue.Core.Diagnostics
{
    /// <summary>
    /// Runtime capability detection: Windows version, whether ConPTY is available, and
    /// which shells were found. Used to drive graceful fallback and the diagnostics UI.
    /// </summary>
    public sealed class CompatibilityInfo
    {
        /// <summary>ConPTY (CreatePseudoConsole) ships in Windows 10 build 17763 (1809).</summary>
        public const int ConPtyMinimumBuild = 17763;

        public Version OsVersion;
        public int OsBuild;
        public bool ConPtySupported;
        public string ClrVersion;
        public List<ShellInfo> Shells;

        public static CompatibilityInfo Detect()
        {
            CompatibilityInfo info = new CompatibilityInfo();
            try
            {
                info.OsVersion = Environment.OSVersion.Version;
                info.OsBuild = info.OsVersion.Build;
            }
            catch
            {
                info.OsVersion = new Version(0, 0);
                info.OsBuild = 0;
            }
            info.ConPtySupported = info.OsBuild >= ConPtyMinimumBuild;
            try { info.ClrVersion = Environment.Version.ToString(); } catch { info.ClrVersion = "unknown"; }
            try { info.Shells = new ShellDiscovery().DiscoverAll(); }
            catch { info.Shells = new List<ShellInfo>(); }
            return info;
        }

        public string BuildReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NIXUE CMD - Diagnostic Report");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Operating system : Windows " + (OsVersion != null ? OsVersion.ToString() : "?"));
            sb.AppendLine("OS build         : " + OsBuild);
            sb.AppendLine("ConPTY backend   : " + (ConPtySupported ? "Supported" : "NOT supported (requires build " + ConPtyMinimumBuild + "+)"));
            sb.AppendLine(".NET CLR         : " + ClrVersion);
            sb.AppendLine();
            sb.AppendLine("Detected shells:");
            if (Shells != null)
            {
                foreach (ShellInfo s in Shells)
                {
                    string line = "  - " + s.DisplayName.PadRight(20);
                    if (s.Available) line += "AVAILABLE  " + s.ExecutablePath;
                    else line += "unavailable (" + s.UnavailableReason + ")";
                    sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }
    }
}
