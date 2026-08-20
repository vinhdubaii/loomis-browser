using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using RemiBrowser.Interop;

namespace RemiBrowser.Views
{
    /// <summary>
    /// A single WebView2 pointed at Edge's own password manager page. WebView2
    /// has no .NET API to list saved passwords, so this reuses Edge's real UI
    /// for viewing, searching, and editing them (search, expand a site, delete,
    /// reveal after Windows Hello) instead of building a custom one from
    /// scratch. The only two things Remi Browser actually controls are the
    /// autosave/autofill toggles already in Settings, Autofill and passwords.
    ///
    /// Must run against App.WebViewEnvironments.NormalEnvironment, the same
    /// profile normal browsing tabs use. Any other environment (or a fresh
    /// one) would read an empty profile and show nothing saved.
    /// </summary>
    public partial class PasswordManagerWindow : Window
    {
        private readonly WebView2 _webView = new();

        public PasswordManagerWindow()
        {
            InitializeComponent();
            WindowMaximizeFix.Apply(this);
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            ContentHost.Children.Add(_webView);

            await _webView.EnsureCoreWebView2Async(App.WebViewEnvironments.NormalEnvironment);
            _webView.CoreWebView2.Navigate("edge://password-manager/passwords");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            _webView.Dispose();
        }
    }
}
