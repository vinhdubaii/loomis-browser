using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemiBrowser.Models;

namespace RemiBrowser.Views
{
    public partial class LibraryPanel : UserControl
    {
        public event EventHandler<string>? OpenUrlRequested;
        public event EventHandler? CloseRequested;

        public LibraryPanel()
        {
            InitializeComponent();
        }

        public async System.Threading.Tasks.Task RefreshAsync()
        {
            ListItems.Items.Clear();

            if (BookmarksTabButton.IsChecked == true)
            {
                foreach (var bookmark in await App.Bookmarks.GetAllAsync())
                    ListItems.Items.Add(BuildRow(bookmark.Title, bookmark.Url, () => OpenUrlRequested?.Invoke(this, bookmark.Url)));
            }
            else if (HistoryTabButton.IsChecked == true)
            {
                foreach (var item in await App.History.GetRecentAsync())
                    ListItems.Items.Add(BuildRow(item.Title, item.Url, () => OpenUrlRequested?.Invoke(this, item.Url)));
            }
            else if (DownloadsTabButton.IsChecked == true)
            {
                foreach (var download in App.Downloads.Downloads)
                {
                    var status = download.State switch
                    {
                        DownloadState.Completed => "Completed",
                        DownloadState.Failed => "Failed",
                        _ => $"{download.ProgressPercent:0}%"
                    };
                    ListItems.Items.Add(BuildRow(download.FileName, $"{status} · {download.Url}", null));
                }
            }
        }

        private Border BuildRow(string title, string subtitle, Action? onClick)
        {
            var border = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = onClick != null ? Cursors.Hand : Cursors.Arrow
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(title) ? subtitle : title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            border.Child = stack;
            if (onClick != null)
                border.MouseLeftButtonDown += (_, _) => onClick();

            return border;
        }

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // BookmarksTabButton has IsChecked="True" in XAML, so WPF raises this
            // Checked event once synchronously *during* InitializeComponent() —
            // before ListItems (declared further down in the same XAML) has been
            // wired up yet, which would NullReferenceException in RefreshAsync().
            // IsLoaded is still false at that point, so this guard skips only that
            // one spurious early call; MainWindow already calls RefreshAsync()
            // explicitly whenever the panel is actually opened (LibraryButton_Click),
            // and every real user click on these tabs happens after the panel is
            // loaded, so real tab switches are unaffected.
            if (!IsLoaded) return;
            await RefreshAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
