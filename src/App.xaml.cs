using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RemiBrowser.Services;

namespace RemiBrowser
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

        public App()
        {
            // App-wide safety net: without these, ANY unhandled exception anywhere
            // (a background Task, an "async void" event handler from WebView2
            // events, etc.) kills the whole process with an opaque native error
            // dialog (0xe0434352) and no indication of what actually went wrong.
            // These handlers turn that into a readable message box instead, and
            // for DispatcherUnhandledException (the common case — anything that
            // eventually resumes on the UI thread) they let the app keep running.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Something went wrong, but Remi Browser will keep running.\n\n{e.Exception}",
                "Remi Browser - Unexpected error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Remi Browser hit a fatal error and needs to close.\n\n{e.ExceptionObject}",
                "Remi Browser - Fatal error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
        }

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
                    "Remi Browser");
                Directory.CreateDirectory(AppDataFolder);

                Settings = new SettingsService(Path.Combine(AppDataFolder, "settings.json"));
                await Settings.LoadAsync();

                Services.ThemeService.Apply(Settings.Current.Theme);

                History = new HistoryService(Path.Combine(AppDataFolder, "browser.db"));
                await History.InitializeAsync();

                Bookmarks = new BookmarkService(Path.Combine(AppDataFolder, "browser.db"));
                await Bookmarks.InitializeAsync();

                SearchEngines = new SearchEngineService(Settings);

                WebViewEnvironments = new WebViewEnvironmentService(AppDataFolder, Settings);
                await WebViewEnvironments.InitializeNormalEnvironmentAsync();

                Downloads = new DownloadService(Settings);

                Updates = new UpdateService("vinhdubaii", "remi-browser");

                // Fire-and-forget background update check; never blocks startup.
                _ = Updates.CheckForUpdateAsync();

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Remi Browser failed to start.\n\n{ex}",
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
