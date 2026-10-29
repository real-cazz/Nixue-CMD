using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nixue.Core.Config
{
    /// <summary>
    /// Loads and saves <see cref="AppSettings"/> as JSON. Loading never throws: a
    /// missing file yields defaults, and a corrupt file is backed up and replaced
    /// with defaults so the application always starts.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly string _path;

        public SettingsStore() : this(AppPaths.SettingsFile) { }

        public SettingsStore(string path)
        {
            _path = path;
        }

        public string Path { get { return _path; } }

        /// <summary>The last load result, for diagnostics.</summary>
        public string LastLoadStatus { get; private set; }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    LastLoadStatus = "No settings file; using defaults.";
                    return new AppSettings();
                }

                string text = File.ReadAllText(_path, Encoding.UTF8);
                object parsed;
                if (!Json.TryParse(text, out parsed))
                {
                    BackupCorrupt(text);
                    LastLoadStatus = "Settings file was not valid JSON; backed up and reset to defaults.";
                    return new AppSettings();
                }

                Dictionary<string, object> obj = parsed as Dictionary<string, object>;
                if (obj == null)
                {
                    BackupCorrupt(text);
                    LastLoadStatus = "Settings file root was not an object; backed up and reset to defaults.";
                    return new AppSettings();
                }

                AppSettings s = AppSettings.FromJson(obj);
                LastLoadStatus = "Loaded.";
                return s;
            }
            catch (Exception ex)
            {
                LastLoadStatus = "Failed to read settings: " + ex.Message + " (using defaults).";
                return new AppSettings();
            }
        }

        /// <summary>Atomically writes settings. Returns false on failure (never throws).</summary>
        public bool Save(AppSettings settings)
        {
            try
            {
                settings.Validate();
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string json = Json.Serialize(settings.ToJson(), true);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));

                // Replace atomically where possible.
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
                File.Move(tmp, _path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void BackupCorrupt(string text)
        {
            try
            {
                string bak = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.WriteAllText(bak, text, new UTF8Encoding(false));
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}
