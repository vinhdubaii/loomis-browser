using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using RemiBrowser.Models;

namespace RemiBrowser.Services
{
    /// <summary>
    /// Downloads, unpacks, installs, and manages browser extensions on top
    /// of WebView2's (experimental) extension APIs. Extensions only ever
    /// load into the Normal CoreWebView2Profile — see
    /// WebViewEnvironmentService, which only sets AreBrowserExtensionsEnabled
    /// on NormalEnvironment. A singleton owned by App, mirroring the shape
    /// of BookmarkService/DownloadService.
    /// </summary>
    public class ExtensionService
    {
        private readonly string _extensionsRootFolder;
        private readonly HttpClient _httpClient = new();

        // Populated once a Normal-profile CoreWebView2 exists; MainWindow
        // calls AttachProfile after its first tab's CoreWebView2 is ready,
        // same pattern as DownloadService.Attach.
        private CoreWebView2Profile? _profile;

        /// <summary>
        /// Raised after RemoveAsync actually removes an extension, so
        /// MainWindow can drop its pinned toolbar icon (if any) without
        /// SettingsWindow having to reach into MainWindow directly.
        /// </summary>
        public event EventHandler<string>? ExtensionRemoved;

        public ExtensionService(string appDataFolder)
        {
            _extensionsRootFolder = Path.Combine(appDataFolder, "Extensions");
            Directory.CreateDirectory(_extensionsRootFolder);
        }

        public void AttachProfile(CoreWebView2Profile profile) => _profile = profile;

        /// <summary>
        /// Full store-install pipeline: download the .crx by ID, unpack it,
        /// register it with WebView2, and return the parsed metadata. Throws
        /// on any failure — callers (ContextMenuActionHandler.AddToRemi) are
        /// expected to catch via the existing RunSafe wrapper and show a
        /// message box, not this service.
        /// </summary>
        public async Task<InstalledExtension> InstallFromStoreAsync(string extensionId, ExtensionStoreKind store)
        {
            if (_profile == null)
                throw new InvalidOperationException("No active WebView2 profile to install into yet.");

            var crxBytes = await DownloadCrxAsync(extensionId, store);
            var extractFolder = Path.Combine(_extensionsRootFolder, extensionId);
            UnpackCrx(crxBytes, extractFolder);

            await _profile.AddBrowserExtensionAsync(extractFolder);

            var extension = ParseManifest(extractFolder)
                ?? throw new InvalidOperationException("Installed extension has no readable manifest.json.");
            return extension;
        }

        public async Task<List<InstalledExtension>> GetInstalledAsync()
        {
            // Cross-reference WebView2's live extension list (for
            // Id/IsEnabled) against each extension's own extracted folder
            // (for Name/Version/Icon/Permissions) - WebView2's own
            // CoreWebView2BrowserExtension type doesn't expose manifest
            // details itself.
            var result = new List<InstalledExtension>();
            if (_profile == null) return result;

            var live = await _profile.GetBrowserExtensionsAsync();
            foreach (var ext in live)
            {
                var folder = Path.Combine(_extensionsRootFolder, ext.Id);
                var parsed = Directory.Exists(folder) ? ParseManifest(folder) : null;
                if (parsed == null) continue;

                parsed.IsEnabled = ext.IsEnabled;
                result.Add(parsed);
            }
            return result;
        }

        public async Task SetEnabledAsync(string extensionId, bool enabled)
        {
            if (_profile == null) return;
            var live = await _profile.GetBrowserExtensionsAsync();
            var match = live.FirstOrDefault(e => e.Id == extensionId);
            if (match != null)
                match.IsEnabled = enabled;
        }

        public async Task RemoveAsync(string extensionId)
        {
            if (_profile == null) return;
            var live = await _profile.GetBrowserExtensionsAsync();
            var match = live.FirstOrDefault(e => e.Id == extensionId);
            if (match != null)
                await match.RemoveAsync();

            var folder = Path.Combine(_extensionsRootFolder, extensionId);
            if (Directory.Exists(folder))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch { /* best-effort, matches CleanupPrivateEnvironment's style */ }
            }

            ExtensionRemoved?.Invoke(this, extensionId);
        }

        // ============================= CRX download =============================

        private async Task<byte[]> DownloadCrxAsync(string extensionId, ExtensionStoreKind store)
        {
            // The store's own product version is baked into the update-check
            // URL (see below) — pulling it from the actual running engine
            // (same call ShowAbout already uses) means this never hardcodes
            // a prodversion that looks stale forever.
            var fullVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(
                WebViewEnvironmentService.FixedRuntimeFolder);
            var majorVersion = fullVersion.Split('.').FirstOrDefault() is { Length: > 0 } major ? major : "120";

            // Unofficial "update check" endpoints — there is no documented
            // public API for downloading a .crx by extension ID. Best-effort;
            // may break without notice if either store changes its endpoint.
            var url = store == ExtensionStoreKind.ChromeWebStore
                ? $"https://clients2.google.com/service/update2/crx?response=redirect&acceptformat=crx2,crx3&prodversion={majorVersion}.0&x=id%3D{extensionId}%26uc"
                : $"https://edge.microsoft.com/extensionwebstorebase/v1/crx?response=redirect&prod=chromiumcrx&prodchannel=&prodversion={majorVersion}.0.0.0&lang=en&acceptformat=crx2,crx3&x=id%3D{extensionId}%26installsource%3Dondemand%26uc";

            byte[] bytes;
            try
            {
                bytes = await _httpClient.GetByteArrayAsync(url);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Couldn't reach the extension store to download this extension: {ex.Message}", ex);
            }

            if (bytes.Length < 12 || bytes[0] != 'C' || bytes[1] != 'r' || bytes[2] != '2' || bytes[3] != '4')
                throw new InvalidDataException(
                    "Downloaded file is not a valid CRX package (bad magic header). The store's download endpoint may have changed.");

            return bytes;
        }

        // ============================= CRX3 unpack =============================

        private static void UnpackCrx(byte[] crx, string destinationFolder)
        {
            // Layout: 4 bytes magic "Cr24", 4 bytes version (uint32 LE),
            // 4 bytes header length N (uint32 LE), N bytes protobuf header
            // (skipped - no signature verification needed for this
            // personal-install flow; Chrome itself doesn't require it for
            // sideloaded extensions either), then a plain zip archive for
            // the rest of the file.
            var version = BitConverter.ToUInt32(crx, 4);
            if (version != 3)
                throw new NotSupportedException($"Unsupported CRX version {version} — only CRX3 is supported.");

            var headerLength = BitConverter.ToInt32(crx, 8);
            var zipStart = 12 + headerLength;

            if (Directory.Exists(destinationFolder))
                Directory.Delete(destinationFolder, recursive: true);
            Directory.CreateDirectory(destinationFolder);

            using var zipStream = new MemoryStream(crx, zipStart, crx.Length - zipStart, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(destinationFolder);
        }

        // ============================= manifest.json parsing =============================

        private InstalledExtension? ParseManifest(string extractFolder)
        {
            var manifestPath = Path.Combine(extractFolder, "manifest.json");
            if (!File.Exists(manifestPath)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;

            var id = new DirectoryInfo(extractFolder).Name;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            var manifestVersion = root.TryGetProperty("manifest_version", out var mv) ? mv.GetInt32() : 2;

            string? iconPath = null;
            if (root.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Object)
            {
                var best = icons.EnumerateObject()
                    .Select(p => (Size: int.TryParse(p.Name, out var s) ? s : 0, Value: p.Value.GetString()))
                    .Where(p => !string.IsNullOrEmpty(p.Value))
                    .OrderByDescending(p => p.Size)
                    .FirstOrDefault();
                if (best.Value != null)
                    iconPath = Path.Combine(extractFolder, best.Value.Replace('/', Path.DirectorySeparatorChar));
            }

            var permissions = new List<string>();
            var siteAccess = new List<string>();

            void ClassifyEntries(JsonElement array)
            {
                foreach (var entry in array.EnumerateArray())
                {
                    var text = entry.GetString();
                    if (string.IsNullOrEmpty(text)) continue;
                    // URL match patterns look like "scheme://host/path" (with *
                    // wildcards) or the special "<all_urls>" - anything else is
                    // a plain API permission name like "storage" or "tabs".
                    if (text.Contains("://") || text == "<all_urls>")
                        siteAccess.Add(text);
                    else
                        permissions.Add(text);
                }
            }

            if (root.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
                ClassifyEntries(perms);
            if (manifestVersion == 3 && root.TryGetProperty("host_permissions", out var hostPerms) && hostPerms.ValueKind == JsonValueKind.Array)
                ClassifyEntries(hostPerms);

            string? optionsPageUrl = null;
            if (root.TryGetProperty("options_page", out var opV2))
                optionsPageUrl = ToChromeExtensionUrl(id, opV2.GetString());
            else if (root.TryGetProperty("options_ui", out var opV3) &&
                     opV3.TryGetProperty("page", out var opV3Page))
                optionsPageUrl = ToChromeExtensionUrl(id, opV3Page.GetString());

            return new InstalledExtension
            {
                Id = id,
                Name = name,
                Version = version,
                Description = description,
                FolderPath = extractFolder,
                IconPath = iconPath,
                Permissions = permissions,
                SiteAccess = siteAccess,
                OptionsPageUrl = optionsPageUrl
            };
        }

        // WebView2 serves an installed extension's own files under
        // chrome-extension://{id}/{relativePath} - same scheme Chrome uses.
        private static string? ToChromeExtensionUrl(string extensionId, string? relativePath) =>
            string.IsNullOrEmpty(relativePath) ? null : $"chrome-extension://{extensionId}/{relativePath.TrimStart('/')}";
    }
}
