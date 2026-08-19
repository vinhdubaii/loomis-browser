using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RemiBrowser.Models;
using RemiBrowser.Services;

namespace RemiBrowser.Views
{
    /// <summary>
    /// Zen Browser-inspired gradient editor: a blank canvas where the user drags
    /// up to 3 color-stop markers to any free 2D position, picks each stop's
    /// color from presets or a hex box, and sees a live preview. Used inside
    /// SettingsWindow's "Custom Themes" panel. No texture/noise slider or
    /// rotation/angle knob — out of scope for this pass (see TASKS.md 3A).
    /// </summary>
    public partial class GradientCanvasControl : UserControl
    {
        private const int MaxStops = 3;

        private static readonly string[] PresetColors =
        {
            "#FF6B6B", "#FFD93D", "#6BCB77", "#4D96FF",
            "#9B5DE5", "#F15BB5", "#00BBF9", "#FEE440"
        };

        public List<GradientColorStop> ColorStops { get; private set; } = new();

        private readonly Dictionary<GradientColorStop, Thumb> _stopThumbs = new();
        private GradientColorStop? _selectedStop;
        private bool _suppressHexBoxHandler;

        public GradientCanvasControl()
        {
            InitializeComponent();
            BuildSwatchRow();
        }

        /// <summary>Called by SettingsWindow when the dialog opens/reloads settings.</summary>
        public void LoadStops(IEnumerable<GradientColorStop> stops)
        {
            ColorStops = stops.Take(MaxStops)
                .Select(s => new GradientColorStop { X = s.X, Y = s.Y, Hex = s.Hex })
                .ToList();

            _selectedStop = null;
            StopEditorPanel.Visibility = Visibility.Collapsed;
            RebuildCanvas();
        }

        // ============================= Swatches =============================

        private void BuildSwatchRow()
        {
            SwatchRow.Children.Clear();
            foreach (var hex in PresetColors)
            {
                var swatchHex = hex; // local copy for the closure below
                var swatch = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                    Background = SafeBrush(swatchHex),
                    BorderBrush = (Brush)FindResource("ChromeBorderBrush"),
                    BorderThickness = new Thickness(1)
                };
                swatch.MouseLeftButtonDown += (_, _) => ApplyColorToSelectedStop(swatchHex);
                SwatchRow.Children.Add(swatch);
            }
        }

        // ============================= Canvas / thumbs =============================

        private void StopCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RebuildCanvas();

        private void RebuildCanvas()
        {
            StopCanvas.Children.Clear();
            _stopThumbs.Clear();

            foreach (var stop in ColorStops)
                AddThumbForStop(stop);

            UpdatePreviewBackground();
            UpdateAddButtonState();
        }

        private void AddThumbForStop(GradientColorStop stop)
        {
            var isSelected = ReferenceEquals(stop, _selectedStop);
            var size = isSelected ? 26.0 : 20.0;

            var thumb = new Thumb
            {
                Width = size,
                Height = size,
                Cursor = Cursors.SizeAll,
                Background = SafeBrush(stop.Hex),
                Template = BuildDotTemplate(isSelected)
            };

            PositionThumb(thumb, stop);

            thumb.DragDelta += (_, e) => OnThumbDragDelta(thumb, stop, e);
            thumb.PreviewMouseLeftButtonDown += (_, _) => SelectStop(stop);

            StopCanvas.Children.Add(thumb);
            _stopThumbs[stop] = thumb;
        }

        private void OnThumbDragDelta(Thumb thumb, GradientColorStop stop, DragDeltaEventArgs e)
        {
            var canvasWidth = StopCanvas.ActualWidth;
            var canvasHeight = StopCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            var newLeft = Canvas.GetLeft(thumb) + e.HorizontalChange;
            var newTop = Canvas.GetTop(thumb) + e.VerticalChange;

            var centerX = Math.Clamp(newLeft + thumb.Width / 2, 0, canvasWidth);
            var centerY = Math.Clamp(newTop + thumb.Height / 2, 0, canvasHeight);

            stop.X = centerX / canvasWidth;
            stop.Y = centerY / canvasHeight;

            Canvas.SetLeft(thumb, centerX - thumb.Width / 2);
            Canvas.SetTop(thumb, centerY - thumb.Height / 2);

            UpdatePreviewBackground();
        }

        private void PositionThumb(Thumb thumb, GradientColorStop stop)
        {
            var canvasWidth = StopCanvas.ActualWidth;
            var canvasHeight = StopCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                // Before the first layout pass ActualWidth/Height are still 0;
                // fall back to the Border's declared size from XAML so stops
                // still land in roughly the right place on first render.
                canvasWidth = PreviewBorder.Width;
                canvasHeight = PreviewBorder.Height;
            }

            Canvas.SetLeft(thumb, stop.X * canvasWidth - thumb.Width / 2);
            Canvas.SetTop(thumb, stop.Y * canvasHeight - thumb.Height / 2);
        }

        /// <summary>
        /// A Thumb's default look has no built-in "colored circle" — this builds a
        /// minimal template that just paints an Ellipse from the Thumb's own
        /// Background (set per-instance to that stop's color), with a thicker
        /// accent-colored ring when selected. The selection ring's color is a
        /// one-time snapshot of the current theme's AccentBrush rather than a
        /// live DynamicResource binding — an accepted, minor limitation for this
        /// Settings-only editor control (see TASKS.md notes on scope).
        /// </summary>
        private ControlTemplate BuildDotTemplate(bool isSelected)
        {
            var template = new ControlTemplate(typeof(Thumb));

            var ellipseFactory = new FrameworkElementFactory(typeof(Ellipse));
            // Bound by property name (string), not nameof(Thumb.Background), since
            // FrameworkElementFactory bindings are resolved at template-apply time
            // against whatever the TemplatedParent turns out to be (a Thumb here).
            ellipseFactory.SetBinding(Shape.FillProperty,
                new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            ellipseFactory.SetValue(Shape.StrokeProperty,
                isSelected ? (Brush)FindResource("AccentBrush") : Brushes.White);
            ellipseFactory.SetValue(Shape.StrokeThicknessProperty, isSelected ? 3.0 : 2.0);

            template.VisualTree = ellipseFactory;
            return template;
        }

        // ============================= Add / remove / select =============================

        private void AddStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (ColorStops.Count >= MaxStops) return;

            var (x, y) = ColorStops.Count switch
            {
                0 => (0.5, 0.5),
                1 => (0.22, 0.3),
                _ => (0.78, 0.7)
            };

            var stop = new GradientColorStop { X = x, Y = y, Hex = PresetColors[ColorStops.Count % PresetColors.Length] };
            ColorStops.Add(stop);
            _selectedStop = stop;
            RebuildCanvas();
            ShowEditorFor(stop);
        }

        private void RemoveStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStop == null) return;

            ColorStops.Remove(_selectedStop);
            _selectedStop = null;
            StopEditorPanel.Visibility = Visibility.Collapsed;
            RebuildCanvas();
        }

        private void SelectStop(GradientColorStop stop)
        {
            _selectedStop = stop;
            RebuildCanvas(); // re-render so the newly selected thumb gets its ring
            ShowEditorFor(stop);
        }

        private void ShowEditorFor(GradientColorStop stop)
        {
            StopEditorPanel.Visibility = Visibility.Visible;
            _suppressHexBoxHandler = true;
            HexBox.Text = stop.Hex;
            _suppressHexBoxHandler = false;
        }

        // ============================= Color editing =============================

        private void ApplyColorToSelectedStop(string hex)
        {
            if (_selectedStop == null) return;

            _selectedStop.Hex = hex;
            _suppressHexBoxHandler = true;
            HexBox.Text = hex;
            _suppressHexBoxHandler = false;

            if (_stopThumbs.TryGetValue(_selectedStop, out var thumb))
                thumb.Background = SafeBrush(hex);

            UpdatePreviewBackground();
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressHexBoxHandler || _selectedStop == null) return;

            var hex = HexBox.Text.Trim();
            if (!IsValidHex(hex)) return; // don't apply a partial/invalid value while the user is still typing

            _selectedStop.Hex = hex;
            if (_stopThumbs.TryGetValue(_selectedStop, out var thumb))
                thumb.Background = SafeBrush(hex);

            UpdatePreviewBackground();
        }

        // ============================= Preview / helpers =============================

        private void UpdatePreviewBackground()
        {
            PreviewBorder.Background = GradientThemeService.BuildBackgroundBrush(ColorStops)
                ?? (Brush)FindResource("SurfaceAltBrush");
        }

        private void UpdateAddButtonState()
        {
            AddStopButton.IsEnabled = ColorStops.Count < MaxStops;
            MaxStopsHint.Visibility = ColorStops.Count >= MaxStops ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsValidHex(string hex) => Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$");

        private static Brush SafeBrush(string hex)
        {
            try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
            catch { return Brushes.Gray; }
        }
    }
}
