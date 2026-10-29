using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nixue.App.Sessions;

namespace Nixue.App.UI
{
    /// <summary>
    /// A single tab: its shell session, its terminal view and the compact header shown
    /// in the tab strip (label, a running-status dot and a close affordance).
    /// </summary>
    public sealed class TerminalTab
    {
        public TerminalSession Session { get; private set; }
        public TerminalView View { get; private set; }
        public Border Header { get; private set; }

        private readonly TextBlock _label;
        private readonly Ellipse _dot;
        private readonly Button _close;
        private bool _active;
        private ThemeResources _theme;

        public event Action<TerminalTab> Activated;
        public event Action<TerminalTab> CloseRequested;

        public TerminalTab(TerminalSession session, TerminalView view, ThemeResources theme)
        {
            Session = session;
            View = view;
            _theme = theme;

            _dot = new Ellipse();
            _dot.Width = 7; _dot.Height = 7;
            _dot.VerticalAlignment = VerticalAlignment.Center;
            _dot.Margin = new Thickness(0, 0, 7, 0);

            _label = new TextBlock();
            _label.Text = session.Title;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.FontSize = 12;

            _close = new Button();
            _close.Content = ""; // close (Segoe MDL2)
            _close.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            _close.Width = 18; _close.Height = 18;
            _close.Margin = new Thickness(8, 0, 0, 0);
            _close.Padding = new Thickness(0);
            _close.FontSize = 11;
            _close.FontWeight = FontWeights.Bold;
            _close.BorderThickness = new Thickness(0);
            _close.Background = Brushes.Transparent;
            _close.Cursor = Cursors.Hand;
            _close.Focusable = false;
            _close.Click += delegate { if (CloseRequested != null) CloseRequested(this); };

            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            content.Margin = new Thickness(12, 0, 8, 0);
            content.Children.Add(_dot);
            content.Children.Add(_label);
            content.Children.Add(_close);

            Header = new Border();
            Header.Height = 30;
            Header.Child = content;
            Header.BorderThickness = new Thickness(0, 2, 0, 0);
            Header.Cursor = Cursors.Hand;
            Header.MouseLeftButtonDown += delegate { if (Activated != null) Activated(this); };

            session.TitleChanged += OnTitleChanged;
            session.Exited += OnExited;

            ApplyTheme(theme);
            UpdateVisual();
        }

        public bool IsActive
        {
            get { return _active; }
            set { _active = value; UpdateVisual(); }
        }

        public void SetLabel(string text)
        {
            _label.Text = text;
        }

        private void OnTitleChanged(string title)
        {
            // Keep a concise label: shell name plus a short title hint.
            string shell = Session.Shell.DisplayName;
            if (!string.IsNullOrEmpty(title) && !title.Equals(shell, StringComparison.OrdinalIgnoreCase))
                _label.Text = shell;
            else
                _label.Text = shell;
        }

        private void OnExited(int code)
        {
            _label.Text = Session.Shell.DisplayName + " (exited)";
            UpdateVisual();
        }

        public void ApplyTheme(ThemeResources theme)
        {
            _theme = theme;
            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (_theme == null) return;
            if (_active)
            {
                Header.Background = _theme.SecondaryPanelBrush;
                Header.BorderBrush = _theme.AccentBrush;
                _label.Foreground = _theme.ForegroundBrush;
            }
            else
            {
                Header.Background = _theme.TabBarBrush;
                Header.BorderBrush = Brushes.Transparent;
                _label.Foreground = _theme.SecondaryForegroundBrush;
            }
            _close.Foreground = _theme.SecondaryForegroundBrush;

            bool running = Session.State == SessionState.Running;
            _dot.Fill = running ? _theme.SuccessBrush : _theme.SecondaryForegroundBrush;
        }

        public void RefreshStatus()
        {
            UpdateVisual();
        }
    }
}
