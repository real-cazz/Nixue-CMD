using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using Nixue.Core.Diagnostics;

namespace Nixue.App.UI
{
    /// <summary>
    /// Registers (and unregisters) an "Open with Nixue CMD" right-click entry for
    /// all file types, folders, and folder backgrounds. Everything is written under
    /// <c>HKEY_CURRENT_USER\Software\Classes</c>, so it is per-user and needs no
    /// administrator rights, and it is fully reversible. This only ever runs when the
    /// user explicitly asks for it from Settings.
    /// </summary>
    public static class ContextMenuInstaller
    {
        private const string Verb = "Nixue";
        private const string Label = "Open with Nixue CMD";

        private static string ExePath()
        {
            try
            {
                Assembly a = Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return a.Location;
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "Nixue.exe");
        }

        public static bool IsInstalled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\*\shell\" + Verb + @"\command"))
                {
                    if (k == null) return false;
                    object v = k.GetValue(null);
                    return v != null && !string.IsNullOrEmpty(v.ToString());
                }
            }
            catch { return false; }
        }

        public static void Install()
        {
            string exe = ExePath();
            string fileCmd = "\"" + exe + "\" \"%1\"";
            string dirCmd = "\"" + exe + "\" \"%1\"";
            string bgCmd = "\"" + exe + "\" \"%V\"";

            WriteVerb(@"Software\Classes\*\shell\" + Verb, exe, fileCmd);
            WriteVerb(@"Software\Classes\Directory\shell\" + Verb, exe, dirCmd);
            WriteVerb(@"Software\Classes\Directory\Background\shell\" + Verb, exe, bgCmd);
            Log.Info("Context menu installed.");
        }

        private static void WriteVerb(string basePath, string exe, string command)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(basePath))
            {
                if (k == null) throw new InvalidOperationException("Could not create " + basePath);
                k.SetValue(null, Label);
                k.SetValue("Icon", exe);
            }
            using (RegistryKey c = Registry.CurrentUser.CreateSubKey(basePath + @"\command"))
            {
                if (c == null) throw new InvalidOperationException("Could not create command key");
                c.SetValue(null, command);
            }
        }

        public static void Uninstall()
        {
            DeleteTree(@"Software\Classes\*\shell\" + Verb);
            DeleteTree(@"Software\Classes\Directory\shell\" + Verb);
            DeleteTree(@"Software\Classes\Directory\Background\shell\" + Verb);
            Log.Info("Context menu removed.");
        }

        private static void DeleteTree(string path)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
            catch { }
        }
    }
}
