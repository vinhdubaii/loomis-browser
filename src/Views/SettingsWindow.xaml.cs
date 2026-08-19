using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        public SettingsWindow()
        {
            InitializeComponent();
            Interop.WindowMaximizeFix.Apply(this);

            _allPanels = new List<StackPanel> { PanelGeneral, PanelAppearance, PanelPrivacy, PanelDownloads, PanelCustomThemes, PanelAbout };
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
            // Same InitializeComponent-ordering guard as LibraryPanel: NavGeneral has
            // IsChecked="True" in XAML, which fires this Checked handler once during
            // InitializeComponent(), before _allPanels has been built yet (it's built
            // in the constructor body, right after InitializeComponent() returns).
            if (_allPanels == null) return;
            if (!string.IsNullOrEmpty(SearchBox.Text)) return; // search view takes priority

            ShowOnlyPanel(sender switch
            {
                _ when ReferenceEquals(sender, NavGeneral) => PanelGeneral,
                _ when ReferenceEquals(sender, NavAppearance) => PanelAppearance,
                _ when ReferenceEquals(sender, NavPrivacy) => PanelPrivacy,
                _ when ReferenceEquals(sender, NavDownloads) => PanelDownloads,
                _ when ReferenceEquals(sender, NavCustomThemes) => PanelCustomThemes,
                _ when ReferenceEquals(sender, NavAbout) => PanelAbout,
                _ => PanelGeneral
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

            s.ShowBookmarkBar = ShowBookmarkBarCheck.IsChecked == true;

            var newTheme = (AppTheme)ThemeCombo.SelectedIndex;
            s.Theme = newTheme;

            s.SecureDns.Mode = DnsCustomOption.IsChecked == true ? SecureDnsMode.Custom
                : DnsAutomaticOption.IsChecked == true ? SecureDnsMode.Automatic
                : SecureDnsMode.Off;

            if (DnsProviderCombo.SelectedItem is ComboBoxItem dnsItem && dnsItem.Tag is string tag)
                s.SecureDns.Provider = tag;
            s.SecureDns.CustomTemplate = DnsCustomTemplateBox.Text.Trim();

            s.Downloads.Location = DownloadLocationBox.Text;
            s.Downloads.AskWhereToSaveEachFile = AskWhereToSaveCheck.IsChecked == true;
            s.Downloads.ShowDownloadsWhenDone = ShowDownloadsWhenDoneCheck.IsChecked == true;

            s.CustomTheme.IsEnabled = CustomThemeEnabledCheck.IsChecked == true;
            s.CustomTheme.ColorStops = GradientCanvas.ColorStops;

            await App.Settings.SaveAsync();

            // Theme takes effect immediately — no restart needed, unlike Secure DNS.
            ThemeService.Apply(newTheme);

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
