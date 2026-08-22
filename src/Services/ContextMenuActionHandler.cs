using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using RemiBrowser.Models;

namespace RemiBrowser.Services
{
    /// <summary>
    /// Implements every action a ContextMenuEntry can be wired to (see
    /// guide section 2.5's group tables). ContextMenuBuilder only decides
    /// *which* items appear; this class decides what actually happens when
    /// one is clicked.
    /// </summary>
    public static class ContextMenuActionHandler
    {
        // ============================= Link group =============================

        public static void OpenLinkInNewWindow(ContextMenuHost host, string linkUri) =>
            host.OpenInNewTab(linkUri);

        public static void CopyLink(string linkUri) =>
            Clipboard.SetText(linkUri);

        public static async void SaveLinkAs(ContextMenuHost host, string linkUri) =>
            await TriggerBrowserDownloadAsync(host.CoreWebView2, linkUri);

        // ============================= Image group =============================

        public static async void SaveImageAs(ContextMenuHost host, string sourceUri) =>
            await TriggerBrowserDownloadAsync(host.CoreWebView2, sourceUri);

        public static void CopyImageLink(string sourceUri) =>
            Clipboard.SetText(sourceUri);

        public static async void CopyImage(ContextMenuHost host, string sourceUri) =>
            await CopyImageToClipboardAsync(host, sourceUri);

        // ============================= Media group =============================

        public static async void ToggleLoop(ContextMenuHost host, Point targetLocation, bool newLoopState)
        {
            var (x, y) = ToCssPixels(host, targetLocation);
            var js = $$"""
                (function() {
                    var el = document.elementFromPoint({{x}}, {{y}});
                    while (el && el.tagName !== 'VIDEO' && el.tagName !== 'AUDIO') el = el.parentElement;
                    if (el) el.loop = {{(newLoopState ? "true" : "false")}};
                })();
                """;
            await host.CoreWebView2.ExecuteScriptAsync(js);
        }

        public static async void ShowAllControls(ContextMenuHost host, Point targetLocation)
        {
            var (x, y) = ToCssPixels(host, targetLocation);
            var js = $$"""
                (function() {
                    var el = document.elementFromPoint({{x}}, {{y}});
                    while (el && el.tagName !== 'VIDEO' && el.tagName !== 'AUDIO') el = el.parentElement;
                    if (el) el.controls = true;
                })();
                """;
            await host.CoreWebView2.ExecuteScriptAsync(js);
        }

        public static async void SaveVideoAs(ContextMenuHost host, string sourceUri) =>
            await TriggerBrowserDownloadAsync(host.CoreWebView2, sourceUri);

        public static async void SaveVideoFrameAs(ContextMenuHost host, Point targetLocation) =>
            await CaptureVideoFrameAsync(host, targetLocation, saveToDisk: true);

        public static async void CopyVideoFrame(ContextMenuHost host, Point targetLocation) =>
            await CaptureVideoFrameAsync(host, targetLocation, saveToDisk: false);

        public static async void TogglePictureInPicture(ContextMenuHost host, Point targetLocation)
        {
            var (x, y) = ToCssPixels(host, targetLocation);
            var js = $$"""
                (function() {
                    var el = document.elementFromPoint({{x}}, {{y}});
                    while (el && el.tagName !== 'VIDEO') el = el.parentElement;
                    if (el) { el.requestPictureInPicture().catch(function() {}); }
                })();
                """;
            await host.CoreWebView2.ExecuteScriptAsync(js);
        }

        // ============================= Selection / Editable group =============================

        public static async void CopySelection(ContextMenuHost host) =>
            await host.CoreWebView2.ExecuteScriptAsync("document.execCommand('copy')");

        public static void SearchWith(ContextMenuHost host, string selectionText)
        {
            var engine = App.SearchEngines.DefaultEngine;
            var url = engine.UrlTemplate.Replace("%s", System.Net.WebUtility.UrlEncode(selectionText));
            host.OpenInNewTab(url);
        }

        public static async void Cut(ContextMenuHost host) =>
            await host.CoreWebView2.ExecuteScriptAsync("document.execCommand('cut')");

        public static async void Paste(ContextMenuHost host)
        {
            // execCommand('paste') is blocked by the page's own script
            // permissions in some contexts (see guide 2.5's note on the
            // Editable group) - if it silently no-ops there's no reliable
            // client-side signal to fall back on without a content-script
            // bridge, which is a bigger follow-up than this menu item alone.
            await host.CoreWebView2.ExecuteScriptAsync("document.execCommand('paste')");
        }

        public static async void SelectAll(ContextMenuHost host) =>
            await host.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll')");

        // ============================= Page group =============================

        public static void Back(ContextMenuHost host) => host.CoreWebView2.GoBack();
        public static void Forward(ContextMenuHost host) => host.CoreWebView2.GoForward();
        public static void Refresh(ContextMenuHost host) => host.CoreWebView2.Reload();

        public static async void SaveAs(ContextMenuHost host)
        {
            var dialog = new SaveFileDialog
            {
                FileName = SuggestFileName(host.CoreWebView2.DocumentTitle, "mhtml"),
                Filter = "Web Page, single file (*.mhtml)|*.mhtml"
            };
            if (dialog.ShowDialog(host.OwnerWindow) != true)
                return;

            // WebView2 doesn't expose page-save (HTML/MHTML) as a first-class
            // API in this SDK, but the DevTools Protocol's Page.captureSnapshot
            // does exactly what a real browser's "Save page as > Webpage,
            // single file" does: one self-contained .mhtml with every
            // sub-resource inlined.
            var resultJson = await host.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Page.captureSnapshot", "{\"format\":\"mhtml\"}");
            using var doc = JsonDocument.Parse(resultJson);
            var mhtml = doc.RootElement.GetProperty("data").GetString() ?? string.Empty;
            await File.WriteAllTextAsync(dialog.FileName, mhtml);
        }

        public static void Print(ContextMenuHost host) => host.CoreWebView2.ShowPrintUI();

        public static async void ViewPageSource(ContextMenuHost host)
        {
            var resultJson = await host.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var html = JsonSerializer.Deserialize<string>(resultJson) ?? string.Empty;

            // TODO: a real syntax-highlighted source-viewer window/tab is a
            // separate, bigger piece of UI (guide 2.5) - not blocking the
            // rest of the context menu, so this is a minimal stand-in until
            // that view exists.
            Clipboard.SetText(html);
            MessageBox.Show(host.OwnerWindow,
                "Page source copied to clipboard. A dedicated source viewer isn't built yet.",
                "View page source", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ============================= Extension group =============================

        public static void AddToRemi(ContextMenuHost host, string pageUri)
        {
            // TODO: wire up to the Part 1 CRX acquisition pipeline (download
            // -> strip header -> unzip -> AddBrowserExtensionAsync) once that
            // service exists - this item only becomes reachable on a Chrome
            // Web Store / Edge Add-ons detail page (see IsExtensionStorePage
            // in ContextMenuBuilder), so it's safe to stub for now.
            MessageBox.Show(host.OwnerWindow,
                "Extension installation isn't implemented yet.",
                "Add to Remi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ============================= Trailing group =============================

        public static void Inspect(ContextMenuHost host) => host.CoreWebView2.OpenDevToolsWindow();

        // ============================= Shared helpers =============================

        /// <summary>
        /// args.Location (from CoreWebView2ContextMenuRequestedEventArgs) is
        /// reported relative to the WebView2 control's own bounds. Page-side
        /// JS coordinate APIs like elementFromPoint work in CSS pixels of the
        /// *visual* viewport at the page's current zoom, so the app's own
        /// ZoomFactor has to be divided out here. This hasn't been verified
        /// against a running app (no Windows/.NET runtime in this dev
        /// environment) - worth double-checking against a real right-click
        /// once this builds, especially at non-100% Windows display scaling.
        /// </summary>
        private static (double x, double y) ToCssPixels(ContextMenuHost host, Point location)
        {
            var zoom = host.WebView.ZoomFactor;
            if (zoom <= 0) zoom = 1.0;
            return (location.X / zoom, location.Y / zoom);
        }

        private static async Task TriggerBrowserDownloadAsync(CoreWebView2 coreWebView2, string url)
        {
            // WebView2 has no direct "download this URL" API in this SDK, so
            // this recreates what a real browser's "Save link/image/video as"
            // does under the hood: synthesize a temporary <a download>
            // element and .click() it from the page's own script context.
            // That's a real download request made *as the page*, so it
            // carries cookies/referrer correctly (unlike a bare out-of-band
            // HTTP fetch would) - it fires CoreWebView2.DownloadStarting,
            // which is already wired up app-wide via App.Downloads.Attach
            // (see DownloadService.cs / MainWindow.CreateNewTabAsync), so
            // the normal Save-As dialog / download list just works with no
            // extra plumbing here.
            var escapedUrl = JsonSerializer.Serialize(url);
            var js = $$"""
                (function() {
                    var a = document.createElement('a');
                    a.href = {{escapedUrl}};
                    a.download = '';
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                })();
                """;
            await coreWebView2.ExecuteScriptAsync(js);
        }

        private static async Task CopyImageToClipboardAsync(ContextMenuHost host, string sourceUri)
        {
            var dataUrl = await CaptureImageAsDataUrlAsync(host.CoreWebView2, sourceUri);
            if (string.IsNullOrEmpty(dataUrl))
            {
                ShowCanvasTaintedMessage(host.OwnerWindow, "Copy image");
                return;
            }

            SetClipboardImageFromDataUrl(dataUrl);
        }

        private static async Task<string?> CaptureImageAsDataUrlAsync(CoreWebView2 coreWebView2, string sourceUri)
        {
            var escapedUrl = JsonSerializer.Serialize(sourceUri);
            var js = $$"""
                (function() {
                    return new Promise(function(resolve) {
                        var img = new Image();
                        img.crossOrigin = 'anonymous';
                        img.onload = function() {
                            var canvas = document.createElement('canvas');
                            canvas.width = img.naturalWidth;
                            canvas.height = img.naturalHeight;
                            canvas.getContext('2d').drawImage(img, 0, 0);
                            try {
                                resolve(canvas.toDataURL('image/png'));
                            } catch (e) {
                                resolve(null);
                            }
                        };
                        img.onerror = function() { resolve(null); };
                        img.src = {{escapedUrl}};
                    });
                })();
                """;
            var resultJson = await coreWebView2.ExecuteScriptAsync(js);
            return JsonSerializer.Deserialize<string>(resultJson);
        }

        private static async Task CaptureVideoFrameAsync(ContextMenuHost host, Point targetLocation, bool saveToDisk)
        {
            var (x, y) = ToCssPixels(host, targetLocation);
            var js = $$"""
                (function() {
                    var el = document.elementFromPoint({{x}}, {{y}});
                    while (el && el.tagName !== 'VIDEO') el = el.parentElement;
                    if (!el) return null;
                    var canvas = document.createElement('canvas');
                    canvas.width = el.videoWidth;
                    canvas.height = el.videoHeight;
                    canvas.getContext('2d').drawImage(el, 0, 0);
                    try {
                        return canvas.toDataURL('image/png');
                    } catch (e) {
                        return null;
                    }
                })();
                """;
            var resultJson = await host.CoreWebView2.ExecuteScriptAsync(js);
            var dataUrl = JsonSerializer.Deserialize<string>(resultJson);

            if (string.IsNullOrEmpty(dataUrl))
            {
                ShowCanvasTaintedMessage(host.OwnerWindow, saveToDisk ? "Save video frame as" : "Copy video frame");
                return;
            }

            if (saveToDisk)
            {
                var dialog = new SaveFileDialog { FileName = "frame.png", Filter = "PNG image (*.png)|*.png" };
                if (dialog.ShowDialog(host.OwnerWindow) != true)
                    return;

                var base64 = dataUrl[(dataUrl.IndexOf(',') + 1)..];
                await File.WriteAllBytesAsync(dialog.FileName, Convert.FromBase64String(base64));
            }
            else
            {
                SetClipboardImageFromDataUrl(dataUrl);
            }
        }

        private static void SetClipboardImageFromDataUrl(string dataUrl)
        {
            var base64 = dataUrl[(dataUrl.IndexOf(',') + 1)..];
            var bytes = Convert.FromBase64String(base64);

            using var stream = new MemoryStream(bytes);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Clipboard.SetImage(decoder.Frames[0]);
        }

        private static void ShowCanvasTaintedMessage(Window owner, string actionTitle) =>
            MessageBox.Show(owner,
                "This couldn't be captured - the source doesn't allow reading its pixel data " +
                "cross-origin (no Access-Control-Allow-Origin header), or it's DRM-protected.",
                actionTitle, MessageBoxButton.OK, MessageBoxImage.Information);

        private static string SuggestFileName(string? title, string extension)
        {
            var name = string.IsNullOrWhiteSpace(title) ? "page" : title;
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return $"{name}.{extension}";
        }
    }
}
