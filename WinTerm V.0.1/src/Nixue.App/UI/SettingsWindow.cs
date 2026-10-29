using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nixue.Core.Config;
using Nixue.Core.Diagnostics;
using Nixue.Core.Shells;

namespace Nixue.App.UI
{
    /// <summary>
    /// A compact, dark settings panel that mirrors the app's geometry. It edits a copy
    /// applied back to the live <see cref="AppSettings"/> only when the user accepts.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ThemeResources _theme;
        private readonly List<ShellInfo> _shells;

        private ComboBox _theme_cb, _shell_cb, _cursor_cb, _verbosity_cb, _fontCombo;
        private TextBox _ifont, _tfont, _promptSym, _startDir, _scrollback;
        private Slider _fontSize, _lineSpacing, _opacity, _blinkRate;
        private TextBlock _fontSizeVal, _lineSpacingVal, _opacityVal, _blinkRateVal;
        private CheckBox _showTab, _showStatus, _reduceAnim, _customPrompt, _showPath, _cursorBlink,
            _bell, _copySelect, _pasteWarn, _newTabCwd, _psProfile,
            _confirmClose, _newTabLast, _restoreBounds, _startMax, _startFull, _logging;
        private string _fontFile;

        public SettingsWindow(AppSettings settings, ThemeResources theme, List<ShellInfo> shells)
        {
            _settings = settings;
            _theme = theme;
            _shells = shells;

            Title = "Nixue CMD - Settings";
            Width = 560; Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = _theme.AppBackgroundBrush;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ModernStyles.Apply(this, _theme);
            Build();
            LoadValues();
        }

        private void Build()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.Padding = new Thickness(16);
            StackPanel panel = new StackPanel();

            panel.Children.Add(Section("Appearance"));
            _theme_cb = Combo(new string[] { "Nixue Dark", "Midnight", "Monochrome", "High Contrast" });
            panel.Children.Add(Row("Theme", _theme_cb));
            _ifont = Text();
            panel.Children.Add(Row("Interface font", _ifont));
            _tfont = Text();
            panel.Children.Add(Row("Terminal font (comma-separated fallbacks)", _tfont));

            // Custom font: a dropdown of the .ttf/.otf files found in the fonts folder.
            _fontCombo = Combo(new string[0]);
            _fontCombo.SelectionChanged += delegate { OnFontComboChanged(); };
            Grid fontGrid = new Grid();
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_fontCombo, 0);
            Button openFonts = FlatButton("Open fonts folder", delegate { OpenFontsFolder(); });
            Grid.SetColumn(openFonts, 1);
            fontGrid.Children.Add(_fontCombo);
            fontGrid.Children.Add(openFonts);
            panel.Children.Add(Row("Custom font (drop .ttf files in the fonts folder, then pick one)", fontGrid));

            _fontSize = Slide(6, 72, 1, out _fontSizeVal);
            panel.Children.Add(Row("Terminal font size", WithValue(_fontSize, _fontSizeVal)));
            _lineSpacing = Slide(0.8, 2.0, 0.05, out _lineSpacingVal);
            panel.Children.Add(Row("Line spacing", WithValue(_lineSpacing, _lineSpacingVal)));
            _opacity = Slide(0.3, 1.0, 0.05, out _opacityVal);
            panel.Children.Add(Row("Window opacity (needs reopen for full effect)", WithValue(_opacity, _opacityVal)));
            _showTab = Check("Show tab bar");
            panel.Children.Add(_showTab);
            _showStatus = Check("Show status bar");
            panel.Children.Add(_showStatus);
            _reduceAnim = Check("Reduce animations");
            panel.Children.Add(_reduceAnim);

            panel.Children.Add(Section("Terminal"));
            _shell_cb = Combo(new string[] { "Cmd", "WindowsPowerShell", "PowerShell7" });
            panel.Children.Add(Row("Default shell (applies to new tabs)", _shell_cb));
            _startDir = Text();
            panel.Children.Add(Row("Starting directory (blank = home)", _startDir));
            _customPrompt = Check("Custom minimal prompt (applies to new tabs)");
            panel.Children.Add(_customPrompt);
            _promptSym = Text();
            panel.Children.Add(Row("Prompt symbol", _promptSym));
            _showPath = Check("Show current directory before the prompt (applies to new tabs)");
            panel.Children.Add(_showPath);
            _cursor_cb = Combo(new string[] { "Block", "Beam", "Underline" });
            panel.Children.Add(Row("Cursor style", _cursor_cb));
            _cursorBlink = Check("Cursor blinks");
            panel.Children.Add(_cursorBlink);
            _blinkRate = Slide(100, 2000, 10, out _blinkRateVal);
            panel.Children.Add(Row("Cursor blink rate (ms)", WithValue(_blinkRate, _blinkRateVal)));
            _scrollback = Text();
            panel.Children.Add(Row("Scrollback limit (lines)", _scrollback));
            _bell = Check("Audible bell");
            panel.Children.Add(_bell);
            _copySelect = Check("Copy on select");
            panel.Children.Add(_copySelect);
            _pasteWarn = Check("Warn on multiline paste");
            panel.Children.Add(_pasteWarn);
            _newTabCwd = Check("New tabs open in current directory (best effort)");
            panel.Children.Add(_newTabCwd);
            _psProfile = Check("Load user PowerShell profile");
            panel.Children.Add(_psProfile);

            panel.Children.Add(Section("Behaviour"));
            _confirmClose = Check("Confirm before closing a running tab");
            panel.Children.Add(_confirmClose);
            _newTabLast = Check("Open a new tab when the last one closes (otherwise quit)");
            panel.Children.Add(_newTabLast);
            _restoreBounds = Check("Restore window size and position");
            panel.Children.Add(_restoreBounds);
            _startMax = Check("Start maximised");
            panel.Children.Add(_startMax);
            _startFull = Check("Start fullscreen");
            panel.Children.Add(_startFull);

            panel.Children.Add(Section("Advanced"));
            _logging = Check("Enable diagnostic logging");
            panel.Children.Add(_logging);
            _verbosity_cb = Combo(new string[] { "error", "warn", "info", "debug" });
            panel.Children.Add(Row("Log verbosity", _verbosity_cb));

            StackPanel adv = new StackPanel();
            adv.Orientation = Orientation.Horizontal;
            adv.Margin = new Thickness(0, 8, 0, 0);
            adv.Children.Add(FlatButton("Open config folder", delegate { OpenFolder(AppPaths.ConfigRoot); }));
            adv.Children.Add(FlatButton("Open log folder", delegate { OpenFolder(AppPaths.LogDirectory); }));
            adv.Children.Add(FlatButton("Diagnostics", delegate { ShowDiagnostics(); }));
            adv.Children.Add(FlatButton("Reset to defaults", delegate { ResetDefaults(); }));
            panel.Children.Add(adv);

            StackPanel ctx = new StackPanel();
            ctx.Orientation = Orientation.Horizontal;
            ctx.Margin = new Thickness(0, 8, 0, 0);
            ctx.Children.Add(FlatButton("Add 'Open with Nixue CMD'", delegate { RegisterContextMenu(true); }));
            ctx.Children.Add(FlatButton("Remove it", delegate { RegisterContextMenu(false); }));
            panel.Children.Add(ctx);

            TextBlock shellsInfo = new TextBlock();
            shellsInfo.Foreground = _theme.SecondaryForegroundBrush;
            shellsInfo.FontSize = 11;
            shellsInfo.Margin = new Thickness(0, 10, 0, 0);
            shellsInfo.TextWrapping = TextWrapping.Wrap;
            System.Text.StringBuilder sb = new System.Text.StringBuilder("Detected shells:  ");
            foreach (ShellInfo s in _shells)
                sb.Append(s.DisplayName).Append(s.Available ? " [ok]   " : " [missing]   ");
            shellsInfo.Text = sb.ToString();
            panel.Children.Add(shellsInfo);

            sv.Content = panel;
            Grid.SetRow(sv, 0);
            root.Children.Add(sv);

            // Footer buttons
            Border footer = new Border();
            footer.Background = _theme.TitleBarBrush;
            footer.BorderBrush = _theme.BorderBrush;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(12, 8, 12, 8);
            StackPanel fp = new StackPanel();
            fp.Orientation = Orientation.Horizontal;
            fp.HorizontalAlignment = HorizontalAlignment.Right;
            Button ok = FlatButton("Save", delegate { Apply(); });
            ok.Background = _theme.AccentBrush;
            ok.Foreground = Brushes.Black;
            Button cancel = FlatButton("Cancel", delegate { DialogResult = false; Close(); });
            fp.Children.Add(cancel);
            fp.Children.Add(ok);
            footer.Child = fp;
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            Content = root;
        }

        // ---- Builders ------------------------------------------------------

        private TextBlock Section(string title)
        {
            TextBlock t = new TextBlock();
            t.Text = title.ToUpperInvariant();
            t.Foreground = _theme.AccentBrush;
            t.FontSize = 11;
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(0, 16, 0, 8);
            return t;
        }

        private UIElement Row(string label, UIElement control)
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 4, 0, 4);
            TextBlock l = new TextBlock();
            l.Text = label;
            l.Foreground = _theme.ForegroundBrush;
            l.FontSize = 12;
            l.Margin = new Thickness(0, 0, 0, 3);
            sp.Children.Add(l);
            sp.Children.Add(control);
            return sp;
        }

        private UIElement WithValue(Slider slider, TextBlock val)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            Grid.SetColumn(slider, 0);
            Grid.SetColumn(val, 1);
            val.Foreground = _theme.SecondaryForegroundBrush;
            val.VerticalAlignment = VerticalAlignment.Center;
            val.HorizontalAlignment = HorizontalAlignment.Right;
            val.FontSize = 12;
            g.Children.Add(slider);
            g.Children.Add(val);
            return g;
        }

        private ComboBox Combo(string[] items)
        {
            ComboBox cb = new ComboBox();
            foreach (string i in items) cb.Items.Add(i);
            cb.FontSize = 12;
            return cb;
        }

        private TextBox Text()
        {
            // Appearance comes from the modern implicit TextBox style.
            return new TextBox();
        }

        private Slider Slide(double min, double max, double tick, out TextBlock val)
        {
            Slider s = new Slider();
            s.Minimum = min; s.Maximum = max;
            s.SmallChange = tick; s.LargeChange = tick * 5;
            s.TickFrequency = tick;
            s.IsSnapToTickEnabled = false;
            val = new TextBlock();
            TextBlock vref = val;
            s.ValueChanged += delegate { vref.Text = s.Value.ToString("0.##", CultureInfo.InvariantCulture); };
            return s;
        }

        private CheckBox Check(string label)
        {
            CheckBox cb = new CheckBox();
            cb.Content = label;
            cb.Margin = new Thickness(0, 5, 0, 5);
            return cb;
        }

        private Button FlatButton(string text, RoutedEventHandler onClick)
        {
            // Appearance comes from the modern implicit Button style.
            Button b = new Button();
            b.Content = text;
            b.Margin = new Thickness(6, 0, 0, 0);
            b.Click += onClick;
            return b;
        }

        // ---- Values --------------------------------------------------------

        private void LoadValues()
        {
            _theme_cb.SelectedItem = _settings.ThemeName;
            _ifont.Text = _settings.InterfaceFont;
            _tfont.Text = _settings.TerminalFont;
            _fontSize.Value = _settings.TerminalFontSize;
            _lineSpacing.Value = _settings.LineSpacing;
            _opacity.Value = _settings.WindowOpacity;
            _showTab.IsChecked = _settings.ShowTabBar;
            _showStatus.IsChecked = _settings.ShowStatusBar;
            _reduceAnim.IsChecked = _settings.ReducedAnimation;

            _shell_cb.SelectedItem = _settings.DefaultShell.ToString();
            _startDir.Text = _settings.StartingDirectory;
            _customPrompt.IsChecked = _settings.CustomPromptEnabled;
            _promptSym.Text = _settings.PromptSymbol;
            _showPath.IsChecked = _settings.ShowPathInPrompt;
            _fontFile = _settings.TerminalFontFile;
            PopulateFonts();
            _cursor_cb.SelectedItem = _settings.CursorStyle.ToString();
            _cursorBlink.IsChecked = _settings.CursorBlink;
            _blinkRate.Value = _settings.CursorBlinkRateMs;
            _scrollback.Text = _settings.ScrollbackLimit.ToString(CultureInfo.InvariantCulture);
            _bell.IsChecked = _settings.BellEnabled;
            _copySelect.IsChecked = _settings.CopyOnSelect;
            _pasteWarn.IsChecked = _settings.MultilinePasteWarning;
            _newTabCwd.IsChecked = _settings.NewTabInCurrentDirectory;
            _psProfile.IsChecked = _settings.LoadPowerShellProfile;

            _confirmClose.IsChecked = _settings.ConfirmCloseRunningTab;
            _newTabLast.IsChecked = _settings.NewTabWhenLastClosed;
            _restoreBounds.IsChecked = _settings.RestoreWindowBounds;
            _startMax.IsChecked = _settings.StartMaximized;
            _startFull.IsChecked = _settings.StartFullscreen;

            _logging.IsChecked = _settings.DiagnosticLogging;
            _verbosity_cb.SelectedItem = _settings.LogVerbosity;
        }

        private void Apply()
        {
            _settings.ThemeName = _theme_cb.SelectedItem as string ?? _settings.ThemeName;
            _settings.InterfaceFont = _ifont.Text;
            _settings.TerminalFont = _tfont.Text;
            _settings.TerminalFontSize = _fontSize.Value;
            _settings.LineSpacing = _lineSpacing.Value;
            _settings.WindowOpacity = _opacity.Value;
            _settings.ShowTabBar = _showTab.IsChecked == true;
            _settings.ShowStatusBar = _showStatus.IsChecked == true;
            _settings.ReducedAnimation = _reduceAnim.IsChecked == true;

            _settings.DefaultShell = ParseEnum<ShellKind>(_shell_cb.SelectedItem as string, _settings.DefaultShell);
            _settings.StartingDirectory = _startDir.Text;
            _settings.CustomPromptEnabled = _customPrompt.IsChecked == true;
            _settings.PromptSymbol = _promptSym.Text;
            _settings.ShowPathInPrompt = _showPath.IsChecked == true;
            _settings.TerminalFontFile = _fontFile == null ? "" : _fontFile;
            _settings.CursorStyle = ParseEnum<CursorStyle>(_cursor_cb.SelectedItem as string, _settings.CursorStyle);
            _settings.CursorBlink = _cursorBlink.IsChecked == true;
            _settings.CursorBlinkRateMs = (int)_blinkRate.Value;
            int sb;
            if (int.TryParse(_scrollback.Text, out sb)) _settings.ScrollbackLimit = sb;
            _settings.BellEnabled = _bell.IsChecked == true;
            _settings.CopyOnSelect = _copySelect.IsChecked == true;
            _settings.MultilinePasteWarning = _pasteWarn.IsChecked == true;
            _settings.NewTabInCurrentDirectory = _newTabCwd.IsChecked == true;
            _settings.LoadPowerShellProfile = _psProfile.IsChecked == true;

            _settings.ConfirmCloseRunningTab = _confirmClose.IsChecked == true;
            _settings.NewTabWhenLastClosed = _newTabLast.IsChecked == true;
            _settings.RestoreWindowBounds = _restoreBounds.IsChecked == true;
            _settings.StartMaximized = _startMax.IsChecked == true;
            _settings.StartFullscreen = _startFull.IsChecked == true;

            _settings.DiagnosticLogging = _logging.IsChecked == true;
            _settings.LogVerbosity = _verbosity_cb.SelectedItem as string ?? _settings.LogVerbosity;

            _settings.Validate();
            Log.Configure(_settings.DiagnosticLogging, _settings.LogVerbosity, AppPaths.LogFile);

            DialogResult = true;
            Close();
        }

        private static T ParseEnum<T>(string s, T def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            try { return (T)Enum.Parse(typeof(T), s, true); }
            catch { return def; }
        }

        private void ResetDefaults()
        {
            MessageBoxResult r = MessageBox.Show(this, "Reset all settings to defaults?",
                "Nixue CMD", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
            _settings.ResetToDefaults();
            LoadValues();
        }

        private void OpenFolder(string path)
        {
            try
            {
                AppPaths.EnsureDirectories();
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn("Open folder failed: " + ex.Message); }
        }

        private void ShowDiagnostics()
        {
            string report = CompatibilityInfo.Detect().BuildReport();
            report += "\r\nConfig : " + AppPaths.SettingsFile;
            report += "\r\nLogs   : " + AppPaths.LogFile;
            MessageBox.Show(this, report, "Nixue CMD - Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---- Custom font -----------------------------------------------------

        private const string NamedFontsItem = "(use named fonts above)";

        private void PopulateFonts()
        {
            try { AppPaths.EnsureDirectories(); } catch { }
            _fontCombo.Items.Clear();
            _fontCombo.Items.Add(NamedFontsItem);
            string selected = NamedFontsItem;
            try
            {
                if (Directory.Exists(AppPaths.FontsDirectory))
                {
                    string[] files = Directory.GetFiles(AppPaths.FontsDirectory);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".ttf" && ext != ".otf") continue;
                        string name = Path.GetFileName(f);
                        _fontCombo.Items.Add(name);
                        if (!string.IsNullOrEmpty(_fontFile) &&
                            string.Equals(Path.GetFullPath(f), SafeFull(_fontFile), StringComparison.OrdinalIgnoreCase))
                            selected = name;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Listing fonts failed: " + ex.Message); }
            _fontCombo.SelectedItem = selected;
        }

        private static string SafeFull(string p)
        {
            try { return Path.GetFullPath(p); } catch { return p; }
        }

        private void OnFontComboChanged()
        {
            string sel = _fontCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(sel) || sel == NamedFontsItem) { _fontFile = ""; return; }
            _fontFile = Path.Combine(AppPaths.FontsDirectory, sel);
        }

        private void OpenFontsFolder()
        {
            try
            {
                AppPaths.EnsureDirectories();
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.FontsDirectory + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn("Open fonts folder failed: " + ex.Message); }
        }

        // ---- Context menu ----------------------------------------------------

        private void RegisterContextMenu(bool install)
        {
            try
            {
                if (install)
                {
                    ContextMenuInstaller.Install();
                    MessageBox.Show(this,
                        "'Open with Nixue CMD' has been added to the right-click menu for files and folders.",
                        "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ContextMenuInstaller.Uninstall();
                    MessageBox.Show(this, "'Open with Nixue CMD' has been removed.",
                        "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Context menu change failed: " + ex.Message);
                MessageBox.Show(this, "Could not update the right-click menu:\n\n" + ex.Message,
                    "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
