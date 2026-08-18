using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using LoomisBrowser.Models;
using MessageBox = System.Windows.MessageBox;

namespace LoomisBrowser.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadFromSettings();
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

            AboutText.Text = $"Loomis Browser {App.Updates.CurrentVersion}\n" +
                              "Open source (MIT License) — github.com/vinhdubaii/loomis-browser";
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

        private void ManageEnginesButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Add a custom search engine by name and URL template (must contain %s for the query).\n" +
                "This dialog is a placeholder for the next iteration.",
                "Manage search engines");
        }

        private void ChangeDownloadLocationButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
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
            s.Theme = (AppTheme)ThemeCombo.SelectedIndex;

            s.SecureDns.Mode = DnsCustomOption.IsChecked == true ? SecureDnsMode.Custom
                : DnsAutomaticOption.IsChecked == true ? SecureDnsMode.Automatic
                : SecureDnsMode.Off;

            if (DnsProviderCombo.SelectedItem is ComboBoxItem dnsItem && dnsItem.Tag is string tag)
                s.SecureDns.Provider = tag;
            s.SecureDns.CustomTemplate = DnsCustomTemplateBox.Text.Trim();

            s.Downloads.Location = DownloadLocationBox.Text;
            s.Downloads.AskWhereToSaveEachFile = AskWhereToSaveCheck.IsChecked == true;
            s.Downloads.ShowDownloadsWhenDone = ShowDownloadsWhenDoneCheck.IsChecked == true;

            await App.Settings.SaveAsync();
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
