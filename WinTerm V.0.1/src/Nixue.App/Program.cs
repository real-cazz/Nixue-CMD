using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Nixue.Core.Config;
using Nixue.Core.Diagnostics;
using Nixue.App.UI;

namespace Nixue.App
{
    /// <summary>
    /// The WPF application. Built entirely in code (no XAML) so it compiles with the
    /// in-box C# compiler without a markup-compilation step.
    /// </summary>
    public sealed class NixueApp : Application
    {
        private readonly AppSettings _settings;
        private readonly SettingsStore _store;

        /// <summary>A file or folder passed on the command line ("Open with Nixue CMD").</summary>
        public string StartupPath;

        public NixueApp(AppSettings settings, SettingsStore store)
        {
            _settings = settings;
            _store = store;
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            MainWindow w = new MainWindow(_settings, _store);
            w.StartupPath = StartupPath;
            w.Show();
        }

        internal static string FirstPathArg()
        {
            try
            {
                string[] argv = Environment.GetCommandLineArgs(); // [0] = exe
                for (int i = 1; i < argv.Length; i++)
                {
                    string a = argv[i];
                    if (string.IsNullOrEmpty(a)) continue;
                    if (a.StartsWith("-") || a.StartsWith("/")) continue;
                    if (System.IO.File.Exists(a) || System.IO.Directory.Exists(a)) return a;
                }
            }
            catch { }
            return null;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled UI exception", e.Exception);
            try
            {
                MessageBox.Show(
                    "An unexpected error occurred:\n\n" + e.Exception.Message +
                    "\n\nThe application will try to continue. See the log for details.",
                    "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true; // keep the app alive where possible
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main()
        {
            // Load settings and configure logging before any UI is created.
            SettingsStore store = new SettingsStore();
            AppSettings settings;
            try
            {
                AppPaths.EnsureDirectories();
                settings = store.Load();
            }
            catch
            {
                settings = new AppSettings();
            }

            try
            {
                Log.Configure(settings.DiagnosticLogging, settings.LogVerbosity, AppPaths.LogFile);
                Log.Info("NIXUE CMD starting. " + store.LastLoadStatus);
            }
            catch { }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs args)
            {
                Exception ex = args.ExceptionObject as Exception;
                Log.Error("Fatal unhandled exception", ex);
            };

            CompatibilityInfo compat = CompatibilityInfo.Detect();
            if (!compat.ConPtySupported)
            {
                MessageBox.Show(
                    "Nixue CMD requires the Windows pseudoconsole (ConPTY), available on " +
                    "Windows 10 build " + CompatibilityInfo.ConPtyMinimumBuild + " (1809) or newer.\n\n" +
                    "This system reports build " + compat.OsBuild + ".",
                    "Nixue CMD - Unsupported Windows version",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return 2;
            }

            try
            {
                NixueApp app = new NixueApp(settings, store);
                app.StartupPath = NixueApp.FirstPathArg();
                return app.Run();
            }
            catch (Exception ex)
            {
                Log.Error("Application terminated with an exception", ex);
                try
                {
                    MessageBox.Show("Nixue CMD could not start:\n\n" + ex.Message,
                        "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                return 1;
            }
        }
    }
}
