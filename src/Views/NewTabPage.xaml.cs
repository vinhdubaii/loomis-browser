using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoomisBrowser.Models;

namespace LoomisBrowser.Views
{
    /// <summary>
    /// The "about:newtab" page: a Top Sites grid built from HistoryService,
    /// plus an optional custom background (color / preset / custom image),
    /// editable via the Customize button (BackgroundPickerPopup).
    /// </summary>
    public partial class NewTabPage : UserControl
    {
        public event EventHandler<string>? NavigateRequested;

        public NewTabPage()
        {
            InitializeComponent();
            ApplyBackground();
        }

        public async System.Threading.Tasks.Task RefreshAsync()
        {
            ApplyBackground();

            var topSites = await App.History.GetTopSitesAsync(8);

            TopSitesGrid.Items.Clear();
            EmptyStateText.Visibility = topSites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var site in topSites)
            {
                var tile = BuildTile(site);
                TopSitesGrid.Items.Add(tile);
            }
        }

        private Border BuildTile(TopSiteItem site)
        {
            var border = new Border
            {
                Width = 130,
                Height = 90,
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.White,
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(new TextBlock
            {
                Text = "🌐",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(site.Title) ? site.Domain : site.Title,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                MaxWidth = 110,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            border.Child = stack;
            border.MouseLeftButtonDown += (_, _) => NavigateRequested?.Invoke(this, site.Url);

            var contextMenu = new ContextMenu();
            var removeItem = new MenuItem { Header = "Remove from Top Sites" };
            // Removal is intentionally left as a follow-up: would need a "hidden sites"
            // list in HistoryService so the tile doesn't just reappear next refresh.
            contextMenu.Items.Add(removeItem);
            border.ContextMenu = contextMenu;

            return border;
        }

        private void ApplyBackground()
        {
            var bg = App.Settings.Current.NewTabBackground;

            switch (bg.Type)
            {
                case NewTabBackgroundType.Color when !string.IsNullOrEmpty(bg.Value):
                    RootGrid.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(bg.Value));
                    OverlayBorder.Opacity = 0;
                    break;

                case NewTabBackgroundType.Preset or NewTabBackgroundType.Custom when !string.IsNullOrEmpty(bg.Value):
                    var path = bg.Type == NewTabBackgroundType.Preset
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Backgrounds", bg.Value)
                        : bg.Value;

                    if (File.Exists(path))
                    {
                        RootGrid.Background = new ImageBrush(new BitmapImage(new Uri(path)))
                        {
                            Stretch = Stretch.UniformToFill
                        };
                        OverlayBorder.Opacity = bg.OverlayOpacity;
                    }
                    break;

                default:
                    RootGrid.Background = Brushes.Transparent;
                    OverlayBorder.Opacity = 0;
                    break;
            }
        }

        private async void CustomizeButton_Click(object sender, RoutedEventArgs e)
        {
            var popup = new BackgroundPickerWindow { Owner = Window.GetWindow(this) };
            if (popup.ShowDialog() == true)
            {
                await App.Settings.SaveAsync();
                ApplyBackground();
            }
        }
    }
}
