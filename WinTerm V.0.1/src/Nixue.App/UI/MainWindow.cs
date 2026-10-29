using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Nixue.Core.Config;
using Nixue.Core.Diagnostics;
using Nixue.Core.Shells;
using Nixue.App.Sessions;

namespace Nixue.App.UI
{
    /// <summary>
    /// The main application window: custom Hyprland-inspired chrome, a compact tab
    /// strip, the dominant terminal viewport and an optional status bar. Owns all open
    /// sessions and coordinates shortcuts, settings and shutdown.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly SettingsStore _store;
        private ThemeResources _theme;
        private readonly ShellDiscovery _discovery = new ShellDiscovery();
        private List<ShellInfo> _shells;

        private readonly List<TerminalTab> _tabs = new List<TerminalTab>();
        private TerminalTab _activeTab;

        private Border _rootBorder;
        private StackPanel _tabStrip;
        private Grid _contentGrid;
        private Border _statusBar;
        private TextBlock _statusText;
        private TextBlock _titleShellText;
        private Button _maxButton;

        private bool _fullscreen;
        private WindowState _preFullscreenState = WindowState.Normal;

        /// <summary>Set by the launcher when opened via "Open with Nixue CMD".</summary>
        public string StartupPath;

        private readonly Action<Action> _postToUi;
        private readonly DispatcherTimer _statusTimer;

        public MainWindow(AppSettings settings, SettingsStore store)
        {
            _settings = settings;
            _store = store;
            _theme = new ThemeResources(ThemeLibrary.GetByName(settings.ThemeName));
            _shells = _discovery.DiscoverAll();
            _postToUi = new Action<Action>(PostToUi);

            Title = "Nixue";
            MinWidth = 480;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            try { Icon = IconFactory.CreateWindowIcon(_theme.Accent, _theme.TitleBar); } catch { }

            ApplyWindowChrome();
            BuildUi();
            RestoreSavedBounds();

            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromMilliseconds(750);
            _statusTimer.Tick += delegate { UpdateStatusBar(); };
            _statusTimer.Start();

            Loaded += delegate { OpenInitialTab(); };
            Closing += OnClosing;
            StateChanged += OnStateChanged;
            KeyDown += OnWindowKeyDown;
        }

        private void PostToUi(Action a)
        {
            if (a == null) return;
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        // ---- Chrome --------------------------------------------------------

        private void ApplyWindowChrome()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = _settings.WindowOpacity < 1.0;
            Opacity = _settings.WindowOpacity;
            ResizeMode = ResizeMode.CanResize;

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = 36;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);
        }

        private void BuildUi()
        {
            Background = _theme.AppBackgroundBrush;

            _rootBorder = new Border();
            _rootBorder.Background = _theme.AppBackgroundBrush;
            _rootBorder.BorderBrush = _theme.BorderBrush;
            _rootBorder.BorderThickness = new Thickness(1);

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // title
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tabs
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status

            grid.Children.Add(BuildTitleBar());
            grid.Children.Add(BuildTabBar());
            grid.Children.Add(BuildContent());
            grid.Children.Add(BuildStatusBar());

            _rootBorder.Child = grid;
            Content = _rootBorder;
        }

        private UIElement BuildTitleBar()
        {
            Border bar = new Border();
            bar.Height = 36;
            bar.Background = _theme.TitleBarBrush;
            Grid.SetRow(bar, 0);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon + brand
            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(12, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;

            Image icon = new Image();
            icon.Width = 22; icon.Height = 22;
            icon.Margin = new Thickness(0, 0, 10, 0);
            try { icon.Source = IconFactory.CreateMark(_theme.Accent, _theme.TitleBar); } catch { }
            brand.Children.Add(icon);

            TextBlock brandText = new TextBlock();
            brandText.Text = "NIXUE CMD";
            brandText.FontSize = 12;
            brandText.FontWeight = FontWeights.SemiBold;
            brandText.Foreground = _theme.ForegroundBrush;
            brandText.VerticalAlignment = VerticalAlignment.Center;
            brandText.Margin = new Thickness(0, 0, 0, 0);

            // Letter-spacing-ish via padding on each char is overkill; keep clean.
            brand.Children.Add(brandText);
            Grid.SetColumn(brand, 0);
            g.Children.Add(brand);

            _titleShellText = new TextBlock();
            _titleShellText.Foreground = _theme.SecondaryForegroundBrush;
            _titleShellText.FontSize = 11;
            _titleShellText.VerticalAlignment = VerticalAlignment.Center;
            _titleShellText.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(_titleShellText, 1);
            g.Children.Add(_titleShellText);

            // Window buttons
            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(buttons, 3);

            Button settingsBtn = CaptionButton("", false); // gear (Settings)
            settingsBtn.Click += delegate { OpenSettings(); };
            buttons.Children.Add(settingsBtn);

            Button minBtn = CaptionButton("", false); // minimize
            minBtn.Click += delegate { WindowState = WindowState.Minimized; };
            buttons.Children.Add(minBtn);

            _maxButton = CaptionButton("", false); // maximize
            _maxButton.Click += delegate { ToggleMaximize(); };
            buttons.Children.Add(_maxButton);

            Button closeBtn = CaptionButton("", true); // close
            closeBtn.Click += delegate { Close(); };
            buttons.Children.Add(closeBtn);

            g.Children.Add(buttons);
            bar.Child = g;
            return bar;
        }

        private static readonly FontFamily IconFont =
            new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private Button CaptionButton(string glyph, bool danger)
        {
            Button b = new Button();
            b.Content = glyph;
            b.Width = 46; b.Height = 36;
            b.FontFamily = IconFont;
            b.FontSize = 15;
            b.FontWeight = FontWeights.Bold;
            b.Foreground = _theme.SecondaryForegroundBrush;
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Focusable = false;
            b.Cursor = Cursors.Hand;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            Brush hover = danger ? _theme.ErrorBrush : _theme.SecondaryPanelBrush;
            Brush fg = _theme.SecondaryForegroundBrush;
            b.MouseEnter += delegate { b.Background = hover; b.Foreground = danger ? Brushes.White : _theme.ForegroundBrush; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; b.Foreground = fg; };
            return b;
        }

        private UIElement BuildTabBar()
        {
            Border bar = new Border();
            bar.Background = _theme.TabBarBrush;
            bar.BorderBrush = _theme.BorderBrush;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid.SetRow(bar, 1);
            WindowChrome.SetIsHitTestVisibleInChrome(bar, true);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            ScrollViewer sv = new ScrollViewer();
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _tabStrip = new StackPanel();
            _tabStrip.Orientation = Orientation.Horizontal;
            sv.Content = _tabStrip;
            Grid.SetColumn(sv, 0);
            g.Children.Add(sv);

            StackPanel controls = new StackPanel();
            controls.Orientation = Orientation.Horizontal;
            Grid.SetColumn(controls, 1);

            Button newBtn = TabControlButton(""); // Add
            newBtn.ToolTip = "New tab (Ctrl+Shift+T)";
            newBtn.Click += delegate { NewTab(_settings.DefaultShell); };
            controls.Children.Add(newBtn);

            Button menuBtn = TabControlButton(""); // chevron down
            menuBtn.ToolTip = "Choose shell (Ctrl+Shift+P)";
            menuBtn.Click += delegate { ShowShellMenu(menuBtn); };
            controls.Children.Add(menuBtn);

            g.Children.Add(controls);
            bar.Child = g;
            return bar;
        }

        private Button TabControlButton(string glyph)
        {
            Button b = new Button();
            b.Content = glyph;
            b.Width = 38; b.Height = 30;
            b.FontFamily = IconFont;
            b.FontSize = 15;
            b.FontWeight = FontWeights.Bold;
            b.Foreground = _theme.SecondaryForegroundBrush;
            b.Background = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Focusable = false;
            b.Cursor = Cursors.Hand;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.MouseEnter += delegate { b.Foreground = _theme.AccentBrush; };
            b.MouseLeave += delegate { b.Foreground = _theme.SecondaryForegroundBrush; };
            return b;
        }

        private UIElement BuildContent()
        {
            _contentGrid = new Grid();
            _contentGrid.Background = _theme.TerminalBackgroundBrush;
            Grid.SetRow(_contentGrid, 2);
            return _contentGrid;
        }

        private UIElement BuildStatusBar()
        {
            _statusBar = new Border();
            _statusBar.Background = _theme.TitleBarBrush;
            _statusBar.BorderBrush = _theme.BorderBrush;
            _statusBar.BorderThickness = new Thickness(0, 1, 0, 0);
            _statusBar.Height = 22;
            Grid.SetRow(_statusBar, 3);

            _statusText = new TextBlock();
            _statusText.Foreground = _theme.SecondaryForegroundBrush;
            _statusText.FontSize = 11;
            _statusText.VerticalAlignment = VerticalAlignment.Center;
            _statusText.Margin = new Thickness(10, 0, 0, 0);
            _statusBar.Child = _statusText;

            _statusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            return _statusBar;
        }

        // ---- Tabs ----------------------------------------------------------

        private void OpenInitialTab()
        {
            // Launched via "Open with Nixue CMD" on a file or folder?
            string start = StartupPath;
            if (!string.IsNullOrEmpty(start))
            {
                try
                {
                    if (System.IO.File.Exists(start))
                    {
                        string dir = System.IO.Path.GetDirectoryName(start);
                        NewTabCore(_settings.DefaultShell, dir, start);
                        if (_tabs.Count > 0) return;
                    }
                    else if (System.IO.Directory.Exists(start))
                    {
                        NewTabCore(_settings.DefaultShell, start, null);
                        if (_tabs.Count > 0) return;
                    }
                }
                catch (Exception ex) { Log.Warn("Startup path handling failed: " + ex.Message); }
            }

            NewTab(_settings.DefaultShell);
            if (_tabs.Count == 0)
            {
                // Could not open the default shell; try cmd explicitly for a clear error.
                NewTab(ShellKind.Cmd);
            }
        }

        private void NewTab(ShellKind kind)
        {
            NewTabCore(kind, null, null);
        }

        private void NewTabCore(ShellKind kind, string workingDirOverride, string pasteFilePath)
        {
            ShellInfo shell = FindShell(kind);
            if (shell == null || !shell.Available)
            {
                string reason = shell != null ? shell.UnavailableReason : "Shell not found.";
                MessageBox.Show(this, "That shell is not available.\n\n" + reason,
                    "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                TerminalView view = new TerminalView();
                view.Visibility = Visibility.Collapsed;
                TerminalSession session = new TerminalSession(shell, 80, 24, _settings.ScrollbackLimit, _postToUi, null);
                view.Attach(session, _theme, _settings);

                _contentGrid.Children.Add(view);

                TerminalTab tab = new TerminalTab(session, view, _theme);
                tab.Activated += ActivateTab;
                tab.CloseRequested += CloseTab;
                _tabStrip.Children.Add(tab.Header);
                _tabs.Add(tab);

                view.TitleChanged += delegate { tab.RefreshStatus(); UpdateWindowTitle(); };
                view.SessionExited += delegate { tab.RefreshStatus(); UpdateStatusBar(); };

                string cmdLine = _discovery.BuildCommandLine(shell, _settings);
                string wd = !string.IsNullOrEmpty(workingDirOverride) ? workingDirOverride : ResolveWorkingDirectory();
                session.Start(cmdLine, wd);

                ActivateTab(tab);
                UpdateStatusBar();
                Log.Info("Opened tab: " + shell.DisplayName);

                if (!string.IsNullOrEmpty(pasteFilePath))
                    ShowPastePrompt(tab, pasteFilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to open tab", ex);
                MessageBox.Show(this,
                    "Could not start " + shell.DisplayName + ".\n\n" + ex.Message +
                    "\n\nSee the log for details.",
                    "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Shows an in-terminal banner asking whether to paste the opened file's
        /// contents into the shell. "No" simply dismisses the banner.
        /// </summary>
        private void ShowPastePrompt(TerminalTab tab, string filePath)
        {
            Border banner = new Border();
            banner.Background = _theme.SecondaryPanelBrush;
            banner.BorderBrush = _theme.AccentBrush;
            banner.BorderThickness = new Thickness(0, 0, 0, 1);
            banner.Padding = new Thickness(12, 8, 12, 8);
            banner.VerticalAlignment = VerticalAlignment.Top;

            DockPanel dp = new DockPanel();

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            DockPanel.SetDock(btns, Dock.Right);

            Button yes = BannerButton("Paste", true);
            Button no = BannerButton("No", false);
            btns.Children.Add(yes);
            btns.Children.Add(no);
            dp.Children.Add(btns);

            TextBlock msg = new TextBlock();
            msg.Foreground = _theme.ForegroundBrush;
            msg.FontSize = 12;
            msg.VerticalAlignment = VerticalAlignment.Center;
            msg.TextTrimming = TextTrimming.CharacterEllipsis;
            msg.Text = "Paste the contents of \"" + System.IO.Path.GetFileName(filePath) + "\" into this terminal?";
            dp.Children.Add(msg);

            banner.Child = dp;
            Grid.SetRow(banner, 2);
            _contentGrid.Children.Add(banner);

            yes.Click += delegate
            {
                _contentGrid.Children.Remove(banner);
                try
                {
                    string text = System.IO.File.ReadAllText(filePath);
                    tab.View.PasteText(text);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not read file for paste: " + ex.Message);
                    MessageBox.Show(this, "Could not read the file:\n\n" + ex.Message,
                        "Nixue CMD", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                tab.View.Focus();
            };
            no.Click += delegate
            {
                _contentGrid.Children.Remove(banner);
                tab.View.Focus();
            };
        }

        private Button BannerButton(string text, bool primary)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = 12;
            b.Padding = new Thickness(14, 4, 14, 4);
            b.Margin = new Thickness(8, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.BorderThickness = new Thickness(primary ? 0 : 1);
            b.BorderBrush = _theme.BorderBrush;
            b.Background = primary ? _theme.AccentBrush : _theme.SecondaryPanelBrush;
            b.Foreground = primary ? Brushes.Black : _theme.ForegroundBrush;
            return b;
        }

        private string ResolveWorkingDirectory()
        {
            string dir = _settings.StartingDirectory;
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) return dir;
            try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
            catch { return null; }
        }

        private ShellInfo FindShell(ShellKind kind)
        {
            foreach (ShellInfo s in _shells) if (s.Kind == kind) return s;
            return _discovery.Discover(kind);
        }

        private void ActivateTab(TerminalTab tab)
        {
            if (tab == null) return;
            foreach (TerminalTab t in _tabs)
            {
                bool active = (t == tab);
                t.IsActive = active;
                t.View.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                t.View.SetRenderingActive(active && WindowState != WindowState.Minimized);
            }
            _activeTab = tab;
            tab.View.Focus();
            UpdateWindowTitle();
            UpdateStatusBar();
        }

        private void CloseTab(TerminalTab tab)
        {
            if (tab == null) return;
            if (_settings.ConfirmCloseRunningTab && tab.Session.IsRunning)
            {
                MessageBoxResult r = MessageBox.Show(this,
                    "This tab is still running a process. Close it?",
                    "Nixue CMD", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (r != MessageBoxResult.OK) return;
            }

            int idx = _tabs.IndexOf(tab);
            try { tab.Session.Dispose(); } catch { }
            _contentGrid.Children.Remove(tab.View);
            _tabStrip.Children.Remove(tab.Header);
            _tabs.Remove(tab);

            if (_tabs.Count == 0)
            {
                if (_settings.NewTabWhenLastClosed) NewTab(_settings.DefaultShell);
                else Close();
                return;
            }

            int next = Math.Min(idx, _tabs.Count - 1);
            if (next < 0) next = 0;
            ActivateTab(_tabs[next]);
        }

        private void NextTab(int dir)
        {
            if (_tabs.Count < 2 || _activeTab == null) return;
            int idx = _tabs.IndexOf(_activeTab);
            idx = (idx + dir + _tabs.Count) % _tabs.Count;
            ActivateTab(_tabs[idx]);
        }

        private void ShowShellMenu(UIElement anchor)
        {
            ContextMenu menu = new ContextMenu();
            foreach (ShellInfo shell in _shells)
            {
                MenuItem item = new MenuItem();
                item.Header = shell.DisplayName + (shell.Available ? "" : "  (unavailable)");
                item.IsEnabled = shell.Available;
                ShellKind k = shell.Kind;
                item.Click += delegate { NewTab(k); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ---- Font / fullscreen / window state ------------------------------

        private void AdjustFont(double delta, bool reset)
        {
            double size = reset ? 14.0 : _settings.TerminalFontSize + delta;
            if (size < 6) size = 6;
            if (size > 72) size = 72;
            _settings.TerminalFontSize = size;
            foreach (TerminalTab t in _tabs) t.View.ApplySettings(_settings);
            _store.Save(_settings);
            UpdateStatusBar();
        }

        private void ToggleMaximize()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void ToggleFullscreen()
        {
            if (!_fullscreen)
            {
                _preFullscreenState = WindowState;
                _fullscreen = true;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
            else
            {
                _fullscreen = false;
                WindowState = _preFullscreenState;
            }
            UpdateChromeVisibility();
        }

        private void UpdateChromeVisibility()
        {
            Visibility v = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
            Grid grid = _rootBorder.Child as Grid;
            if (grid != null)
            {
                ((UIElement)grid.Children[0]).Visibility = v; // title
                ((UIElement)grid.Children[1]).Visibility = _settings.ShowTabBar ? v : Visibility.Collapsed; // tabs
            }
            WindowChrome chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null) chrome.CaptionHeight = _fullscreen ? 0 : 36;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (_maxButton != null)
                _maxButton.Content = (WindowState == WindowState.Maximized) ? "" : "";
            // Avoid maximized content spilling under the screen edges.
            if (_rootBorder != null)
            {
                _rootBorder.Padding = (WindowState == WindowState.Maximized)
                    ? new Thickness(7) : new Thickness(0);
            }
            bool active = WindowState != WindowState.Minimized;
            if (_activeTab != null) _activeTab.View.SetRenderingActive(active);
        }

        // ---- Shortcuts -----------------------------------------------------

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys m = Keyboard.Modifiers;
            bool ctrl = (m & ModifierKeys.Control) != 0;
            bool shift = (m & ModifierKeys.Shift) != 0;

            if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; return; }
            if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; return; }

            if (ctrl && shift)
            {
                switch (e.Key)
                {
                    case Key.T: NewTab(_settings.DefaultShell); e.Handled = true; return;
                    case Key.C: if (_activeTab != null) _activeTab.View.CopySelection(); e.Handled = true; return;
                    case Key.V: if (_activeTab != null) _activeTab.View.Paste(); e.Handled = true; return;
                    case Key.P: ShowShellMenu(_tabStrip); e.Handled = true; return;
                    case Key.N: OpenNewWindow(); e.Handled = true; return;
                    case Key.Tab: NextTab(-1); e.Handled = true; return;
                }
            }

            if (ctrl && !shift)
            {
                switch (e.Key)
                {
                    case Key.W: if (_activeTab != null) CloseTab(_activeTab); e.Handled = true; return;
                    case Key.Tab: NextTab(1); e.Handled = true; return;
                    case Key.OemPlus:
                    case Key.Add: AdjustFont(1, false); e.Handled = true; return;
                    case Key.OemMinus:
                    case Key.Subtract: AdjustFont(-1, false); e.Handled = true; return;
                    case Key.D0:
                    case Key.NumPad0: AdjustFont(0, true); e.Handled = true; return;
                    case Key.L: SendClear(); e.Handled = true; return;
                    case Key.OemComma: OpenSettings(); e.Handled = true; return;
                }
            }
        }

        private void SendClear()
        {
            if (_activeTab == null) return;
            if (_activeTab.Session.Shell.Kind == ShellKind.Cmd)
                _activeTab.Session.SendInput("cls\r");
            else
                _activeTab.Session.SendInput("\x0c");
        }

        private void OpenNewWindow()
        {
            try
            {
                MainWindow w = new MainWindow(_settings.Clone(), _store);
                w.Show();
            }
            catch (Exception ex) { Log.Error("Failed to open new window", ex); }
        }

        // ---- Settings / about ----------------------------------------------

        private void OpenSettings()
        {
            SettingsWindow dlg = new SettingsWindow(_settings, _theme, _shells);
            dlg.Owner = this;
            bool? ok = dlg.ShowDialog();
            if (ok == true)
            {
                _store.Save(_settings);
                ReloadTheme();
                foreach (TerminalTab t in _tabs) t.View.ApplySettings(_settings);
                _statusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
                Opacity = _settings.WindowOpacity;
                UpdateChromeVisibility();
                UpdateStatusBar();
            }
        }

        private void ReloadTheme()
        {
            _theme.Apply(ThemeLibrary.GetByName(_settings.ThemeName));
            try { Icon = IconFactory.CreateWindowIcon(_theme.Accent, _theme.TitleBar); } catch { }
            // Rebuild chrome brushes by re-theming the tree.
            Background = _theme.AppBackgroundBrush;
            foreach (TerminalTab t in _tabs) { t.ApplyTheme(_theme); t.View.ApplyTheme(_theme); }
            ReapplyStaticBrushes();
        }

        private void ReapplyStaticBrushes()
        {
            _rootBorder.Background = _theme.AppBackgroundBrush;
            _rootBorder.BorderBrush = _theme.BorderBrush;
            _contentGrid.Background = _theme.TerminalBackgroundBrush;
            _statusBar.Background = _theme.TitleBarBrush;
            _statusBar.BorderBrush = _theme.BorderBrush;
            _statusText.Foreground = _theme.SecondaryForegroundBrush;
            Grid grid = _rootBorder.Child as Grid;
            if (grid != null)
            {
                Border title = grid.Children[0] as Border;
                if (title != null) title.Background = _theme.TitleBarBrush;
                Border tabs = grid.Children[1] as Border;
                if (tabs != null) { tabs.Background = _theme.TabBarBrush; tabs.BorderBrush = _theme.BorderBrush; }
            }
            if (_titleShellText != null) _titleShellText.Foreground = _theme.SecondaryForegroundBrush;
        }

        // ---- Status / title ------------------------------------------------

        private void UpdateWindowTitle()
        {
            string shell = _activeTab != null ? _activeTab.Session.Shell.DisplayName : "";
            Title = string.IsNullOrEmpty(shell) ? "Nixue CMD" : "Nixue CMD - " + shell;
            if (_titleShellText != null) _titleShellText.Text = shell;
        }

        private void UpdateStatusBar()
        {
            if (_statusBar == null || _statusBar.Visibility != Visibility.Visible) return;
            if (_activeTab == null) { _statusText.Text = "No session"; return; }
            TerminalSession s = _activeTab.Session;
            string state;
            switch (s.State)
            {
                case SessionState.Running: state = "running"; break;
                case SessionState.Exited: state = "exited (code " + s.ExitCode + ")"; break;
                case SessionState.Failed: state = "failed"; break;
                default: state = s.State.ToString().ToLowerInvariant(); break;
            }
            _statusText.Text = s.Shell.DisplayName + "   |   " + s.Cols + "x" + s.Rows +
                               "   |   " + state + "   |   " + _settings.TerminalFontSize + "pt" +
                               "   |   " + _settings.ThemeName +
                               "   |   ConPTY";
        }

        // ---- Bounds / shutdown ---------------------------------------------

        private void RestoreSavedBounds()
        {
            if (!_settings.RestoreWindowBounds) { ApplyStartupState(); return; }
            double left = _settings.WindowLeft;
            double top = _settings.WindowTop;
            if (!double.IsNaN(left) && !double.IsNaN(top) &&
                IsOnScreen(left, top, _settings.WindowWidth, _settings.WindowHeight))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left; Top = top;
                Width = _settings.WindowWidth; Height = _settings.WindowHeight;
            }
            ApplyStartupState();
        }

        private void ApplyStartupState()
        {
            if (_settings.StartFullscreen) Dispatcher.BeginInvoke(new Action(ToggleFullscreen));
            else if (_settings.StartMaximized || _settings.WindowMaximized) WindowState = WindowState.Maximized;
        }

        private static bool IsOnScreen(double left, double top, double w, double h)
        {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth;
            double vb = vt + SystemParameters.VirtualScreenHeight;
            // Require the title bar area to be visible.
            double cx = left + Math.Min(w, 120);
            double cy = top + 20;
            return cx > vl && cx < vr && cy > vt && cy < vb;
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (_settings.RestoreWindowBounds && WindowState != WindowState.Minimized)
                {
                    _settings.WindowMaximized = (WindowState == WindowState.Maximized);
                    if (WindowState == WindowState.Normal)
                    {
                        _settings.WindowLeft = Left;
                        _settings.WindowTop = Top;
                        _settings.WindowWidth = Width;
                        _settings.WindowHeight = Height;
                    }
                    _store.Save(_settings);
                }
            }
            catch (Exception ex) { Log.Debug("Saving bounds failed: " + ex.Message); }

            _statusTimer.Stop();
            List<TerminalTab> copy = new List<TerminalTab>(_tabs);
            foreach (TerminalTab t in copy)
            {
                try { t.Session.Dispose(); } catch { }
            }
            _tabs.Clear();
            Log.Info("Window closed; all sessions disposed.");
        }
    }
}
