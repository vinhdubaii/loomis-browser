using System.Windows;
using System.Windows.Input;
using RemiBrowser.Models;

namespace RemiBrowser.Views
{
    /// <summary>
    /// A single-tab-per-window (kept simple on purpose) incognito browser.
    /// Uses App.WebViewEnvironments.PrivateEnvironment so it never touches the
    /// normal profile's cookies, and never calls HistoryService.
    /// </summary>
    public partial class PrivateWindow : Window
    {
        private readonly BrowserTab _tab = new() { IsPrivate = true };

        public PrivateWindow()
        {
            InitializeComponent();
            StateChanged += (_, _) => UpdateRootGridMarginForWindowState();
            UpdateRootGridMarginForWindowState();
            _ = InitializeAsync();
        }

        private void UpdateRootGridMarginForWindowState()
        {
            // Same WindowChrome maximize-overscan fix as MainWindow — see there for details.
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

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            ContentHost.Children.Add(_tab.WebView);

            var env = await App.WebViewEnvironments.GetOrCreatePrivateEnvironmentAsync();
            await _tab.WebView.EnsureCoreWebView2Async(env);

            _tab.WebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                AddressBarTextBox.Text = _tab.WebView.Source?.ToString() ?? string.Empty;
                BackButton.IsEnabled = _tab.WebView.CoreWebView2.CanGoBack;
                ForwardButton.IsEnabled = _tab.WebView.CoreWebView2.CanGoForward;
            };

            // Intentionally no App.History.AddVisitAsync call anywhere in this window.
            _tab.WebView.CoreWebView2.Navigate(App.Settings.Current.HomepageUrl == "about:newtab"
                ? "https://duckduckgo.com"
                : App.Settings.Current.HomepageUrl);
        }

        private void AddressBarTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _tab.WebView.CoreWebView2.Navigate(App.SearchEngines.Resolve(AddressBarTextBox.Text));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => _tab.WebView.CoreWebView2.GoBack();
        private void ForwardButton_Click(object sender, RoutedEventArgs e) => _tab.WebView.CoreWebView2.GoForward();
        private void ReloadButton_Click(object sender, RoutedEventArgs e) => _tab.WebView.CoreWebView2.Reload();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PrivateWindow_Closed(object? sender, System.EventArgs e)
        {
            _tab.WebView.Dispose();
            // Wipe the private profile once the (single, simplified) private window closes.
            App.WebViewEnvironments.CleanupPrivateEnvironment();
        }
    }
}
