using System;
using System.IO;

namespace Nixue.Core.Config
{
    /// <summary>
    /// Resolves the per-user locations the application reads and writes. Everything
    /// lives under Documents\Nixue so the app never needs administrator rights
    /// and never touches machine-wide state.
    /// </summary>
    public static class AppPaths
    {
        public const string AppFolderName = "Nixue";

        private static string _overrideRoot;

        /// <summary>Test hook: redirect the config root to an arbitrary folder.</summary>
        public static void OverrideRootForTesting(string root)
        {
            _overrideRoot = root;
        }

        public static string ConfigRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_overrideRoot)) return _overrideRoot;
                // Settings, logs and fonts live under the user's Documents folder.
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs))
                    docs = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(docs, AppFolderName);
            }
        }

        public static string SettingsFile { get { return Path.Combine(ConfigRoot, "settings.json"); } }
        public static string LogDirectory { get { return Path.Combine(ConfigRoot, "logs"); } }
        public static string LogFile { get { return Path.Combine(LogDirectory, "nixue.log"); } }
        public static string FontsDirectory { get { return Path.Combine(ConfigRoot, "fonts"); } }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(ConfigRoot);
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(FontsDirectory);
        }
    }
}
