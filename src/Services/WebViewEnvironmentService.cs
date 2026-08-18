using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using LoomisBrowser.Models;

namespace LoomisBrowser.Services
{
    /// <summary>
    /// Owns the two CoreWebView2Environment instances used by the app:
    ///   - Normal: fixed UserDataFolder under %AppData%, cookies/session persist across restarts.
    ///   - Private: a fresh temp folder created per app session, deleted on exit.
    /// Every WebView2 control in a normal tab must be initialized against
    /// NormalEnvironment, and every private-window tab against PrivateEnvironment,
    /// so that private browsing never touches the persistent profile.
    ///
    /// NOTE: Secure DNS is applied via AdditionalBrowserArguments, which only
    /// take effect when an environment is created. Changing the DNS setting at
    /// runtime therefore requires an app restart to apply — surfaced in
    /// SettingsWindow as a "Restart to apply" prompt rather than attempted live.
    /// </summary>
    public class WebViewEnvironmentService
    {
        private readonly string _appDataFolder;
        private readonly SettingsService _settings;
        private string? _privateTempFolder;

        public CoreWebView2Environment? NormalEnvironment { get; private set; }
        public CoreWebView2Environment? PrivateEnvironment { get; private set; }

        public WebViewEnvironmentService(string appDataFolder, SettingsService settings)
        {
            _appDataFolder = appDataFolder;
            _settings = settings;
        }

        public async Task InitializeNormalEnvironmentAsync()
        {
            var userDataFolder = Path.Combine(_appDataFolder, "WebView2Profile");
            Directory.CreateDirectory(userDataFolder);

            var options = new CoreWebView2EnvironmentOptions(BuildBrowserArguments());
            NormalEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: options);
        }

        public async Task<CoreWebView2Environment> GetOrCreatePrivateEnvironmentAsync()
        {
            if (PrivateEnvironment != null)
                return PrivateEnvironment;

            _privateTempFolder = Path.Combine(Path.GetTempPath(), "LoomisBrowserPrivate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_privateTempFolder);

            var options = new CoreWebView2EnvironmentOptions(BuildBrowserArguments());
            PrivateEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _privateTempFolder,
                options: options);

            return PrivateEnvironment;
        }

        /// <summary>Deletes the temp profile used for private browsing. Call when the last private window closes.</summary>
        public void CleanupPrivateEnvironment()
        {
            PrivateEnvironment = null;

            if (_privateTempFolder != null && Directory.Exists(_privateTempFolder))
            {
                try { Directory.Delete(_privateTempFolder, recursive: true); }
                catch { /* best-effort cleanup; OS will reclaim temp eventually */ }
            }

            _privateTempFolder = null;
        }

        private string BuildBrowserArguments()
        {
            var dns = _settings.Current.SecureDns;
            if (dns.Mode == SecureDnsMode.Off)
                return string.Empty;

            var template = dns.Provider == "custom"
                ? dns.CustomTemplate
                : SecureDnsSettings.BuiltInProviders.GetValueOrDefault(dns.Provider);

            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            var mode = dns.Mode == SecureDnsMode.Automatic ? "automatic" : "secure";

            return $"--enable-features=DnsOverHttps --dns-over-https-mode={mode} --dns-over-https-templates={template}";
        }
    }
}
