using System;
using System.IO;
using System.Windows;
using LoomisBrowser.Services;

namespace LoomisBrowser
{
    /// <summary>
    /// Application entry point. Responsible for creating the shared, app-lifetime
    /// singletons (settings, history, bookmarks, WebView2 environments) *before*
    /// any window is shown, since MainWindow and PrivateWindow both depend on them.
    /// </summary>
    public partial class App : Application
    {
        public static SettingsService Settings { get; private set; } = null!;
        public static HistoryService History { get; private set; } = null!;
        public static BookmarkService Bookmarks { get; private set; } = null!;
        public static DownloadService Downloads { get; private set; } = null!;
        public static SearchEngineService SearchEngines { get; private set; } = null!;
        public static WebViewEnvironmentService WebViewEnvironments { get; private set; } = null!;
        public static UpdateService Updates { get; private set; } = null!;

        public static string AppDataFolder { get; private set; } = string.Empty;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Loomis Browser");
            Directory.CreateDirectory(AppDataFolder);

            Settings = new SettingsService(Path.Combine(AppDataFolder, "settings.json"));
            await Settings.LoadAsync();

            History = new HistoryService(Path.Combine(AppDataFolder, "browser.db"));
            await History.InitializeAsync();

            Bookmarks = new BookmarkService(Path.Combine(AppDataFolder, "browser.db"));
            await Bookmarks.InitializeAsync();

            SearchEngines = new SearchEngineService(Settings);

            WebViewEnvironments = new WebViewEnvironmentService(AppDataFolder, Settings);
            await WebViewEnvironments.InitializeNormalEnvironmentAsync();

            Downloads = new DownloadService(Settings);

            Updates = new UpdateService("vinhdubaii", "loomis-browser");

            // Fire-and-forget background update check; never blocks startup.
            _ = Updates.CheckForUpdateAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Settings.SaveAsync().GetAwaiter().GetResult();
            base.OnExit(e);
        }
    }
}
