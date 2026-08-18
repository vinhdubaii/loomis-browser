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

            // App.xaml intentionally has no StartupUri: with StartupUri, WPF creates
            // and shows MainWindow as soon as this method first yields (at the first
            // await below), while Settings/History/etc. are still null — MainWindow's
            // constructor reads App.Settings.Current immediately and would crash with
            // a NullReferenceException. Instead we create MainWindow ourselves, after
            // every async init step below has actually finished.
            try
            {
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

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Loomis Browser failed to start.\n\n{ex}",
                    "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Settings.SaveAsync().GetAwaiter().GetResult();
            base.OnExit(e);
        }
    }
}
