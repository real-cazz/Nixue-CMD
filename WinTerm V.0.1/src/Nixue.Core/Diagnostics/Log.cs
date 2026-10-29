using System;
using System.IO;
using System.Text;

namespace Nixue.Core.Diagnostics
{
    public enum LogLevel { Error = 0, Warn = 1, Info = 2, Debug = 3 }

    /// <summary>
    /// A tiny, dependency-free file logger. It is safe to call from any thread, never
    /// throws (a logging failure must not crash the app), keeps the file bounded in
    /// size with a single rotation, and deliberately never records command contents,
    /// clipboard data or environment secrets.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _enabled;
        private static LogLevel _level = LogLevel.Info;
        private const long MaxBytes = 1024 * 1024; // 1 MB then rotate once

        public static void Configure(bool enabled, string verbosity, string path)
        {
            lock (Gate)
            {
                _enabled = enabled;
                _level = ParseLevel(verbosity);
                _path = path;
                if (_enabled && !string.IsNullOrEmpty(_path))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(_path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    }
                    catch { _enabled = false; }
                }
            }
        }

        public static bool Enabled { get { return _enabled; } }
        public static string CurrentPath { get { return _path; } }

        private static LogLevel ParseLevel(string v)
        {
            if (string.IsNullOrEmpty(v)) return LogLevel.Info;
            switch (v.ToLowerInvariant())
            {
                case "error": return LogLevel.Error;
                case "warn": return LogLevel.Warn;
                case "debug": return LogLevel.Debug;
                default: return LogLevel.Info;
            }
        }

        public static void Error(string message) { Write(LogLevel.Error, message, null); }
        public static void Error(string message, Exception ex) { Write(LogLevel.Error, message, ex); }
        public static void Warn(string message) { Write(LogLevel.Warn, message, null); }
        public static void Info(string message) { Write(LogLevel.Info, message, null); }
        public static void Debug(string message) { Write(LogLevel.Debug, message, null); }

        private static void Write(LogLevel level, string message, Exception ex)
        {
            if (!_enabled) return;
            if (level > _level) return;
            try
            {
                lock (Gate)
                {
                    if (string.IsNullOrEmpty(_path)) return;
                    RotateIfNeeded();
                    StringBuilder sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
                    sb.Append(message);
                    if (ex != null)
                    {
                        sb.Append(" :: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    }
                    sb.Append(Environment.NewLine);
                    File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Never let logging take down the app.
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                FileInfo fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = _path + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_path, old);
                }
            }
            catch
            {
                // ignore rotation failures
            }
        }
    }
}
