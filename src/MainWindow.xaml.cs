using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using RemiBrowser.Models;
using RemiBrowser.Views;

namespace RemiBrowser
{
    /// <summary>
    /// Shell window for normal (non-private) browsing. Owns the tab collection,
    /// the unified toolbar/title bar, the collapsible tab strip, the optional
    /// bookmark bar, and the slide-out Library panel. Private windows use the
    /// separate PrivateWindow class instead of this one.
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<BrowserTab> Tabs { get; } = new();
        private BrowserTab? _activeTab;

        public MainWindow()
        {
            InitializeComponent();

            Width = App.Settings.Current.Window.Width;
            Height = App.Settings.Current.Window.Height;
            if (App.Settings.Current.Window.IsMaximized)
                WindowState = WindowState.Maximized;

            // Classic WindowChrome + Maximized-window issue: Windows reserves an
            // invisible "overscan" margin (roughly SystemParameters.WindowResizeBorderThickness
            // + WindowNonClientFrameThickness) around a borderless window when it's
            // maximized, so the actual screen doesn't get overdrawn. WPF does NOT
            // subtract that margin from content automatically, so without this the
            // toolbar/tab strip/window buttons render partly off-screen and look
            // "cut off" whenever the window is maximized. Reapplying the correct
            // margin on every StateChanged fixes it without affecting the Normal
            // (non-maximized) window at all.
            StateChanged += (_, _) => UpdateRootGridMarginForWindowState();
            UpdateRootGridMarginForWindowState();

            LibraryPanelControl.OpenUrlRequested += (_, url) => NavigateActiveTab(url);
            LibraryPanelControl.CloseRequested += (_, _) => LibraryPanelControl.Visibility = Visibility.Collapsed;

            RebuildBookmarkBar();

            _ = CreateNewTabAsync();
        }

        private void UpdateRootGridMarginForWindowState()
        {
            if (WindowState == WindowState.Maximized)
            {
                var resizeBorder = SystemParameters.WindowResizeBorderThickness;
                var frame = SystemParameters.WindowNonClientFrameThickness;
                RootGrid.Margin = new Thickness(
                    resizeBorder.Left + frame.Left,
                    resizeBorder.Top + frame.Top,
                    resizeBorder.Right + frame.Right,
                    resizeBorder.Bottom + frame.Bottom);
            }
            else
            {
                RootGrid.Margin = new Thickness(0);
            }
        }

        // ============================= Tab management =============================

        private async System.Threading.Tasks.Task CreateNewTabAsync(string? initialUrl = null)
        {
            var tab = new BrowserTab { IsPrivate = false };
            Tabs.Add(tab);

            TabContentHost.Children.Add(tab.WebView);
            tab.WebView.Visibility = Visibility.Collapsed;

            await tab.WebView.EnsureCoreWebView2Async(App.WebViewEnvironments.NormalEnvironment);
            WireTabEvents(tab);

            App.Downloads.Attach(tab.WebView.CoreWebView2);

            if (initialUrl != null)
            {
                tab.IsNewTabPage = false;
                tab.WebView.CoreWebView2.Navigate(initialUrl);
            }

            RebuildTabStrip();
            SetActiveTab(tab);
        }

        private void WireTabEvents(BrowserTab tab)
        {
            tab.WebView.CoreWebView2.NavigationStarting += (_, _) => tab.IsLoading = true;

            tab.WebView.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                tab.IsLoading = false;
                tab.Url = tab.WebView.Source?.ToString() ?? tab.Url;
                tab.CanGoBack = tab.WebView.CoreWebView2.CanGoBack;
                tab.CanGoForward = tab.WebView.CoreWebView2.CanGoForward;

                if (e.IsSuccess && !tab.IsPrivate)
                {
                    await App.History.AddVisitAsync(tab.Url, tab.Title, tab.FaviconUrl);
                }

                if (ReferenceEquals(tab, _activeTab))
                    UpdateAddressBarAndButtons(tab);
            };

            tab.WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                tab.Title = string.IsNullOrWhiteSpace(tab.WebView.CoreWebView2.DocumentTitle)
                    ? tab.Url
                    : tab.WebView.CoreWebView2.DocumentTitle;
                RebuildTabStrip();
            };

            tab.WebView.CoreWebView2.FaviconChanged += (_, _) =>
            {
                tab.FaviconUrl = tab.WebView.CoreWebView2.FaviconUri;
            };
        }

        private void SetActiveTab(BrowserTab tab)
        {
            if (_activeTab != null)
            {
                _activeTab.WebView.Visibility = Visibility.Collapsed;
            }

            _activeTab = tab;
            tab.WebView.Visibility = tab.IsNewTabPage ? Visibility.Collapsed : Visibility.Visible;

            ShowOrHideNewTabPage(tab);
            UpdateAddressBarAndButtons(tab);
            RebuildTabStrip();
        }

        private NewTabPage? _newTabPageControl;

        private void ShowOrHideNewTabPage(BrowserTab tab)
        {
            if (tab.IsNewTabPage)
            {
                if (_newTabPageControl == null)
                {
                    _newTabPageControl = new NewTabPage();
                    _newTabPageControl.NavigateRequested += (_, url) => NavigateActiveTab(url);
                    TabContentHost.Children.Add(_newTabPageControl);
                }

                _newTabPageControl.Visibility = Visibility.Visible;
                _ = _newTabPageControl.RefreshAsync();
            }
            else if (_newTabPageControl != null)
            {
                _newTabPageControl.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseTab(BrowserTab tab)
        {
            var index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            TabContentHost.Children.Remove(tab.WebView);
            tab.WebView.Dispose();

            if (Tabs.Count == 0)
            {
                Close();
                return;
            }

            if (ReferenceEquals(_activeTab, tab))
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                SetActiveTab(Tabs[newIndex]);
            }

            RebuildTabStrip();
        }

        /// <summary>Builds the tab strip UI directly (no DataTemplate binding) — only visible when >= 2 tabs, Epiphany-style.</summary>
        private void RebuildTabStrip()
        {
            TabStripItems.Items.Clear();
            ((Border)TabStripItems.Parent).Visibility =
                Tabs.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tab in Tabs)
            {
                var isActive = ReferenceEquals(tab, _activeTab);

                var border = new Border
                {
                    Height = 28,
                    Margin = new Thickness(2, 3, 2, 3),
                    Padding = new Thickness(10, 0, 4, 0),
                    CornerRadius = new CornerRadius(6),
                    Background = isActive
                        ? (Brush)FindResource("TabActiveBrush")
                        : (Brush)FindResource("TabInactiveBrush"),
                    Cursor = Cursors.Hand
                };

                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(tab.Title) ? "New Tab" : tab.Title,
                    MaxWidth = 160,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                });

                var closeButton = new Button
                {
                    Content = "✕",
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(6, 0, 0, 0),
                    Style = (Style)FindResource("ChromeIconButton"),
                    FontSize = 10
                };
                closeButton.Click += (_, _) => CloseTab(tab);
                stack.Children.Add(closeButton);

                border.Child = stack;
                border.MouseLeftButtonDown += (_, _) => SetActiveTab(tab);

                TabStripItems.Items.Add(border);
            }
        }

        // ============================= Address bar =============================

        private void UpdateAddressBarAndButtons(BrowserTab tab)
        {
            AddressBarTextBox.Text = tab.IsNewTabPage ? string.Empty : tab.Url;
            BackButton.IsEnabled = tab.CanGoBack;
            ForwardButton.IsEnabled = tab.CanGoForward;
            LockIcon.Text = tab.Url.StartsWith("https://") ? "🔒" : "";
            _ = UpdateBookmarkStarAsync(tab.Url);
        }

        private async System.Threading.Tasks.Task UpdateBookmarkStarAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) { BookmarkStarButton.Content = "☆"; return; }
            var bookmarked = await App.Bookmarks.IsBookmarkedAsync(url);
            BookmarkStarButton.Content = bookmarked ? "★" : "☆";
        }

        private void AddressBarTextBox_GotFocus(object sender, RoutedEventArgs e) => AddressBarTextBox.SelectAll();

        private void AddressBarTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            NavigateActiveTab(App.SearchEngines.Resolve(AddressBarTextBox.Text));
        }

        private void NavigateActiveTab(string url)
        {
            if (_activeTab == null) return;

            if (url == "about:newtab")
            {
                _activeTab.IsNewTabPage = true;
                _activeTab.WebView.Visibility = Visibility.Collapsed;
                ShowOrHideNewTabPage(_activeTab);
                AddressBarTextBox.Text = string.Empty;
                return;
            }

            _activeTab.IsNewTabPage = false;
            ShowOrHideNewTabPage(_activeTab);
            _activeTab.WebView.Visibility = Visibility.Visible;
            _activeTab.WebView.CoreWebView2.Navigate(url);
        }

        // ============================= Toolbar button handlers =============================

        private void BackButton_Click(object sender, RoutedEventArgs e) => _activeTab?.WebView.CoreWebView2.GoBack();
        private void ForwardButton_Click(object sender, RoutedEventArgs e) => _activeTab?.WebView.CoreWebView2.GoForward();
        private void ReloadButton_Click(object sender, RoutedEventArgs e) => _activeTab?.WebView.CoreWebView2.Reload();
        private async void NewTabButton_Click(object sender, RoutedEventArgs e) => await CreateNewTabAsync();

        private async void BookmarkStarButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTab == null || string.IsNullOrEmpty(_activeTab.Url)) return;

            var isBookmarked = await App.Bookmarks.IsBookmarkedAsync(_activeTab.Url);
            if (isBookmarked)
                await App.Bookmarks.RemoveAsync(_activeTab.Url);
            else
                await App.Bookmarks.AddAsync(_activeTab.Url, _activeTab.Title, _activeTab.FaviconUrl);

            await UpdateBookmarkStarAsync(_activeTab.Url);
            RebuildBookmarkBar();
        }

        private void LibraryButton_Click(object sender, RoutedEventArgs e)
        {
            LibraryPanelControl.Visibility = LibraryPanelControl.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (LibraryPanelControl.Visibility == Visibility.Visible)
                _ = LibraryPanelControl.RefreshAsync();
        }

        // ============================= Hamburger menu (☰) =============================

        private async void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            menu.Items.Add(MenuItem("New Tab", "Ctrl+T", async (_, _) => await CreateNewTabAsync()));
            menu.Items.Add(MenuItem("New Private Window", "Ctrl+Shift+N", (_, _) => OpenPrivateWindow()));
            menu.Items.Add(new Separator());

            var showBookmarkBar = new MenuItem { Header = "Show Bookmark Bar", IsCheckable = true, IsChecked = App.Settings.Current.ShowBookmarkBar };
            showBookmarkBar.Click += async (_, _) =>
            {
                App.Settings.Current.ShowBookmarkBar = showBookmarkBar.IsChecked;
                await App.Settings.SaveAsync();
                RebuildBookmarkBar();
            };
            menu.Items.Add(showBookmarkBar);
            menu.Items.Add(new Separator());

            menu.Items.Add(MenuItem("Find in Page...", "Ctrl+F", (_, _) => _activeTab?.WebView.CoreWebView2.ExecuteScriptAsync("undefined")));
            menu.Items.Add(MenuItem("Print...", "Ctrl+P", (_, _) => _activeTab?.WebView.CoreWebView2.ShowPrintUI()));
            menu.Items.Add(new Separator());

            menu.Items.Add(MenuItem("Settings", null, (_, _) => new SettingsWindow { Owner = this }.ShowDialog()));
            menu.Items.Add(MenuItem("About Remi Browser", null, (_, _) => ShowAbout()));
            menu.Items.Add(new Separator());

            menu.Items.Add(MenuItem("Exit", null, (_, _) => Close()));

            menu.PlacementTarget = MenuButton;
            menu.IsOpen = true;
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private static MenuItem MenuItem(string header, string? gesture, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            item.Click += handler;
            return item;
        }

        private void ShowAbout()
        {
            var version = App.Updates.CurrentVersion;
            // Must pass the same browserExecutableFolder used by NormalEnvironment/
            // PrivateEnvironment — the parameterless overload always looks for the
            // system Evergreen Runtime, which throws WebView2RuntimeNotFoundException
            // on machines that only have our bundled Fixed Version runtime.
            var engineVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(
                Services.WebViewEnvironmentService.FixedRuntimeFolder);
            MessageBox.Show(
                $"Remi Browser {version}\nEngine (Chromium/WebView2): {engineVersion}\n\nOpen source under the MIT License.\ngithub.com/vinhdubaii/remi-browser",
                "About Remi Browser", MessageBoxButton.OK, MessageBoxImage.None);
        }

        private void OpenPrivateWindow()
        {
            var window = new PrivateWindow();
            window.Show();
        }

        // ============================= Bookmark bar =============================

        private async void RebuildBookmarkBar()
        {
            BookmarkBarHost.Visibility = App.Settings.Current.ShowBookmarkBar ? Visibility.Visible : Visibility.Collapsed;
            if (!App.Settings.Current.ShowBookmarkBar) return;

            BookmarkBarItems.Items.Clear();
            var bookmarks = await App.Bookmarks.GetAllAsync();

            foreach (var bookmark in bookmarks.Take(20))
            {
                var button = new Button
                {
                    Content = string.IsNullOrEmpty(bookmark.Title) ? bookmark.Url : bookmark.Title,
                    Margin = new Thickness(2, 0, 2, 0),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 12,
                    Cursor = Cursors.Hand
                };
                button.Click += (_, _) => NavigateActiveTab(bookmark.Url);
                BookmarkBarItems.Items.Add(button);
            }
        }

        // ============================= Window chrome (custom title bar) =============================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Current.Window.Width = Width;
            App.Settings.Current.Window.Height = Height;
            App.Settings.Current.Window.IsMaximized = WindowState == WindowState.Maximized;
            await App.Settings.SaveAsync();
            Close();
        }
    }
}
