using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using RemiBrowser.Interop;
using RemiBrowser.Models;
using RemiBrowser.Services;
using MessageBox = System.Windows.MessageBox;

namespace RemiBrowser.Views
{
    /// <summary>
    /// Chromium/Cromite-style settings: a left sidebar switches between category
    /// panels (General/Appearance/Privacy &amp; Security/Downloads/About) on the
    /// right. Typing in the search box switches to a flattened view: every
    /// field group (the Border elements wrapping each control block, tagged
    /// with keywords) whose Tag contains the search text is shown across ALL
    /// categories at once, regardless of which sidebar item is selected —
    /// mirroring how chrome://settings' search behaves. Clearing the search
    /// box reverts to normal single-category sidebar navigation.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private List<StackPanel> _allPanels = null!;
        private List<Border> _allFieldGroups = null!;

        /// <summary>
        /// The active tab's CoreWebView2.Profile, passed in from MainWindow so
        /// "Delete browsing data..." can operate on the real normal-profile
        /// store. Null when there's genuinely no tab yet (shouldn't happen in
        /// practice — MainWindow always has at least one tab — but guarded
        /// anyway so Settings never crashes over it).
        /// </summary>
        private readonly CoreWebView2Profile? _activeProfile;

        /// <summary>Live URLs of every currently open normal tab, for "Use current pages".</summary>
        private readonly IReadOnlyList<string> _currentTabUrls;

        public SettingsWindow() : this(null, Array.Empty<string>())
        {
        }

        public SettingsWindow(CoreWebView2Profile? activeProfile, IReadOnlyList<string> currentTabUrls)
        {
            InitializeComponent();
            Interop.WindowMaximizeFix.Apply(this);

            _activeProfile = activeProfile;
            _currentTabUrls = currentTabUrls;

            _allPanels = new List<StackPanel> { PanelSearchEngine, PanelAppearance, PanelPrivacy, PanelAutofillPasswords, PanelDefaultBrowser, PanelOnStartup, PanelDownloads, PanelCustomThemes, PanelAbout };
            _allFieldGroups = _allPanels.SelectMany(p => p.Children.OfType<Border>()).ToList();

            LoadFromSettings();
        }

        // ============================= Custom title bar =============================

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { MaximizeButton_Click(sender, e); return; }
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        }

        private void LoadFromSettings()
        {
            var s = App.Settings.Current;

            SearchEngineCombo.ItemsSource = s.SearchEngines;
            SearchEngineCombo.SelectedItem = s.SearchEngines.FirstOrDefault(e => e.Name == s.DefaultSearchEngineName);

            // ---- On startup ----
            switch (s.Startup.Mode)
            {
                case StartupMode.Continue: StartupContinueOption.IsChecked = true; break;
                case StartupMode.ContinueAndNewTab: StartupContinueAndNewTabOption.IsChecked = true; break;
                case StartupMode.SpecificPages: StartupSpecificPagesOption.IsChecked = true; break;
                default: StartupNewTabOption.IsChecked = true; break;
            }
            StartupPagesList.Items.Clear();
            foreach (var page in s.Startup.Pages)
                StartupPagesList.Items.Add(page);
            UpdateStartupPagesPanelVisibility();

            // ---- Default browser ----
            RefreshDefaultBrowserStatus();

            ShowBookmarkBarCheck.IsChecked = s.ShowBookmarkBar;
            ThemeCombo.SelectedIndex = (int)s.Theme;

            switch (s.SecureDns.Mode)
            {
                case SecureDnsMode.Off: DnsOffOption.IsChecked = true; break;
                case SecureDnsMode.Automatic: DnsAutomaticOption.IsChecked = true; break;
                case SecureDnsMode.Custom: DnsCustomOption.IsChecked = true; break;
            }
            SelectComboItemByTag(DnsProviderCombo, s.SecureDns.Provider);
            DnsCustomTemplateBox.Text = s.SecureDns.CustomTemplate ?? string.Empty;

            // ---- Passwords and autofill ----
            OfferSavePasswordsCheck.IsChecked = s.PasswordManager.OfferToSavePasswords;
            AutofillEnabledCheck.IsChecked = s.PasswordManager.AutofillEnabled;

            // ---- Clear on close ----
            ClearOnCloseCheck.IsChecked = s.ClearOnClose.Enabled;
            ClearOnCloseHistoryCheck.IsChecked = s.ClearOnClose.Types.History;
            ClearOnCloseCookiesCheck.IsChecked = s.ClearOnClose.Types.Cookies;
            ClearOnCloseCacheCheck.IsChecked = s.ClearOnClose.Types.Cache;
            ClearOnCloseDownloadHistoryCheck.IsChecked = s.ClearOnClose.Types.DownloadHistory;
            ClearOnCloseAutofillCheck.IsChecked = s.ClearOnClose.Types.AutofillData;
            ClearOnClosePasswordsCheck.IsChecked = s.ClearOnClose.Types.Passwords;
            UpdateClearOnClosePanelVisibility();

            DownloadLocationBox.Text = s.Downloads.Location;
            AskWhereToSaveCheck.IsChecked = s.Downloads.AskWhereToSaveEachFile;
            ShowDownloadsWhenDoneCheck.IsChecked = s.Downloads.ShowDownloadsWhenDone;

            CustomThemeEnabledCheck.IsChecked = s.CustomTheme.IsEnabled;
            GradientCanvas.LoadStops(s.CustomTheme.ColorStops);

            AboutText.Text = $"Remi Browser {App.Updates.CurrentVersion}\n" +
                              "Open source (MIT License) — github.com/vinhdubaii/remi-browser";
        }

        private static void SelectComboItemByTag(ComboBox combo, string tag)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if ((string?)item.Tag == tag)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        // ============================= Sidebar navigation =============================

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            // Same InitializeComponent-ordering guard as LibraryPanel: NavSearchEngine has
            // IsChecked="True" in XAML, which fires this Checked handler once during
            // InitializeComponent(), before _allPanels has been built yet (it's built
            // in the constructor body, right after InitializeComponent() returns).
            if (_allPanels == null) return;
            if (!string.IsNullOrEmpty(SearchBox.Text)) return; // search view takes priority

            ShowOnlyPanel(sender switch
            {
                _ when ReferenceEquals(sender, NavSearchEngine) => PanelSearchEngine,
                _ when ReferenceEquals(sender, NavAppearance) => PanelAppearance,
                _ when ReferenceEquals(sender, NavPrivacy) => PanelPrivacy,
                _ when ReferenceEquals(sender, NavAutofillPasswords) => PanelAutofillPasswords,
                _ when ReferenceEquals(sender, NavDefaultBrowser) => PanelDefaultBrowser,
                _ when ReferenceEquals(sender, NavOnStartup) => PanelOnStartup,
                _ when ReferenceEquals(sender, NavDownloads) => PanelDownloads,
                _ when ReferenceEquals(sender, NavCustomThemes) => PanelCustomThemes,
                _ when ReferenceEquals(sender, NavAbout) => PanelAbout,
                _ => PanelSearchEngine
            });
        }

        private void ShowOnlyPanel(StackPanel target)
        {
            foreach (var panel in _allPanels)
                panel.Visibility = ReferenceEquals(panel, target) ? Visibility.Visible : Visibility.Collapsed;

            foreach (var group in _allFieldGroups)
                group.Visibility = Visibility.Visible; // reset any leftover filtering from a previous search
        }

        // ============================= Search filter =============================

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(query))
            {
                // Revert to whichever sidebar category is currently selected.
                var selected = SidebarNav.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
                if (selected != null) Nav_Checked(selected, new RoutedEventArgs());
                return;
            }

            // Flattened search mode: every panel becomes visible, but only the
            // field groups (Border with matching Tag keywords) inside them show.
            foreach (var panel in _allPanels)
                panel.Visibility = Visibility.Visible;

            foreach (var group in _allFieldGroups)
            {
                var keywords = (group.Tag as string) ?? string.Empty;
                group.Visibility = keywords.ToLowerInvariant().Contains(query)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // Hide whole panels that end up with zero visible field groups, so the
            // section headers ("General", "Appearance"...) don't show up empty.
            foreach (var panel in _allPanels)
            {
                var anyVisible = panel.Children.OfType<Border>().Any(b => b.Visibility == Visibility.Visible);
                if (!anyVisible) panel.Visibility = Visibility.Collapsed;
            }
        }

        // ============================= Field handlers =============================

        // ============================= On startup =============================

        private void StartupOption_Checked(object sender, RoutedEventArgs e) => UpdateStartupPagesPanelVisibility();

        private void UpdateStartupPagesPanelVisibility()
        {
            // Guard: this can fire from XAML during InitializeComponent(), before
            // StartupPagesPanel itself has been assigned yet (same pattern as
            // Nav_Checked's _allPanels guard above).
            if (StartupPagesPanel == null) return;

            StartupPagesPanel.Visibility = StartupSpecificPagesOption.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void AddStartupPageButton_Click(object sender, RoutedEventArgs e) => AddStartupPageFromTextBox();

        private void StartupPageUrlBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                AddStartupPageFromTextBox();
        }

        private void AddStartupPageFromTextBox()
        {
            var url = StartupPageUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (!url.Contains("://"))
                url = "https://" + url;

            StartupPagesList.Items.Add(url);
            StartupPageUrlBox.Text = string.Empty;
        }

        private void RemoveStartupPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (StartupPagesList.SelectedItem != null)
                StartupPagesList.Items.Remove(StartupPagesList.SelectedItem);
        }

        private void UseCurrentPagesButton_Click(object sender, RoutedEventArgs e)
        {
            StartupPagesList.Items.Clear();
            foreach (var url in _currentTabUrls)
            {
                if (!string.IsNullOrWhiteSpace(url) && url != "about:newtab")
                    StartupPagesList.Items.Add(url);
            }
        }

        // ============================= Default browser =============================

        private void RefreshDefaultBrowserStatus()
        {
            var isDefault = DefaultBrowserService.IsDefaultBrowser();

            DefaultBrowserStatusText.Text = isDefault
                ? "✓ Remi is your default browser."
                : "Remi is not currently your default browser.";

            MakeDefaultButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        }

        private void MakeDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            DefaultBrowserService.OpenDefaultAppsSettings();
        }

        /// <summary>
        /// The user picks the default browser in the separate Windows Settings
        /// app, then comes back here — re-check status whenever this window
        /// regains focus so the checkmark updates without needing to reopen
        /// Settings entirely.
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (IsLoaded) RefreshDefaultBrowserStatus();
        }

        // ============================= Passwords / Delete browsing data / Clear on close =============================

        private void DeleteBrowsingDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeProfile == null)
            {
                MessageBox.Show("No active browsing profile is available right now.", "Delete browsing data");
                return;
            }

            var dialog = new DeleteBrowsingDataDialog(_activeProfile) { Owner = this };
            dialog.ShowDialog();
        }

        private void ClearOnCloseCheck_CheckedChanged(object sender, RoutedEventArgs e) => UpdateClearOnClosePanelVisibility();

        private void UpdateClearOnClosePanelVisibility()
        {
            if (ClearOnClosePanel == null) return;
            ClearOnClosePanel.Visibility = ClearOnCloseCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ManageEnginesButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Add a custom search engine by name and URL template (must contain %s for the query).\n" +
                "This dialog is a placeholder for the next iteration.",
                "Manage search engines");
        }

        private void ChangeDownloadLocationButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = DownloadLocationBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                DownloadLocationBox.Text = dialog.SelectedPath;
        }

        private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var info = await App.Updates.CheckForUpdateAsync();
            MessageBox.Show(info == null
                ? "You're on the latest version."
                : $"Version {info.Version} is available. Choose Update from the notification to install it.");
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings.Current;

            if (SearchEngineCombo.SelectedItem is SearchEngine engine)
                s.DefaultSearchEngineName = engine.Name;

            // ---- On startup ----
            s.Startup.Mode = StartupContinueOption.IsChecked == true ? StartupMode.Continue
                : StartupContinueAndNewTabOption.IsChecked == true ? StartupMode.ContinueAndNewTab
                : StartupSpecificPagesOption.IsChecked == true ? StartupMode.SpecificPages
                : StartupMode.NewTab;
            s.Startup.Pages = StartupPagesList.Items.Cast<string>().ToList();

            s.ShowBookmarkBar = ShowBookmarkBarCheck.IsChecked == true;

            var newTheme = (AppTheme)ThemeCombo.SelectedIndex;
            s.Theme = newTheme;

            s.SecureDns.Mode = DnsCustomOption.IsChecked == true ? SecureDnsMode.Custom
                : DnsAutomaticOption.IsChecked == true ? SecureDnsMode.Automatic
                : SecureDnsMode.Off;

            if (DnsProviderCombo.SelectedItem is ComboBoxItem dnsItem && dnsItem.Tag is string tag)
                s.SecureDns.Provider = tag;
            s.SecureDns.CustomTemplate = DnsCustomTemplateBox.Text.Trim();

            // ---- Passwords and autofill ----
            s.PasswordManager.OfferToSavePasswords = OfferSavePasswordsCheck.IsChecked == true;
            s.PasswordManager.AutofillEnabled = AutofillEnabledCheck.IsChecked == true;

            // ---- Clear on close ----
            s.ClearOnClose.Enabled = ClearOnCloseCheck.IsChecked == true;
            s.ClearOnClose.Types.History = ClearOnCloseHistoryCheck.IsChecked == true;
            s.ClearOnClose.Types.Cookies = ClearOnCloseCookiesCheck.IsChecked == true;
            s.ClearOnClose.Types.Cache = ClearOnCloseCacheCheck.IsChecked == true;
            s.ClearOnClose.Types.DownloadHistory = ClearOnCloseDownloadHistoryCheck.IsChecked == true;
            s.ClearOnClose.Types.AutofillData = ClearOnCloseAutofillCheck.IsChecked == true;
            s.ClearOnClose.Types.Passwords = ClearOnClosePasswordsCheck.IsChecked == true;

            s.Downloads.Location = DownloadLocationBox.Text;
            s.Downloads.AskWhereToSaveEachFile = AskWhereToSaveCheck.IsChecked == true;
            s.Downloads.ShowDownloadsWhenDone = ShowDownloadsWhenDoneCheck.IsChecked == true;

            s.CustomTheme.IsEnabled = CustomThemeEnabledCheck.IsChecked == true;
            s.CustomTheme.ColorStops = GradientCanvas.ColorStops;

            await App.Settings.SaveAsync();

            // Theme takes effect immediately — no restart needed, unlike Secure DNS.
            ThemeService.Apply(newTheme);

            // Password/autofill toggles are live CoreWebView2Profile properties —
            // apply immediately rather than waiting for a restart.
            if (_activeProfile != null)
            {
                _activeProfile.IsPasswordAutosaveEnabled = s.PasswordManager.OfferToSavePasswords;
                _activeProfile.IsGeneralAutofillEnabled = s.PasswordManager.AutofillEnabled;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
