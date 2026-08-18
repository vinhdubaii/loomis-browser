using System;
using System.Collections.Generic;
using System.IO;

namespace LoomisBrowser.Models
{
    public enum SecureDnsMode
    {
        Off,
        Automatic,
        Custom
    }

    public enum AppTheme
    {
        System,
        Light,
        Dark
    }

    public enum NewTabBackgroundType
    {
        None,
        Color,
        Preset,
        Custom
    }

    public class SecureDnsSettings
    {
        public SecureDnsMode Mode { get; set; } = SecureDnsMode.Off;

        /// <summary>One of: cloudflare, google, quad9, cleanbrowsing, custom.</summary>
        public string Provider { get; set; } = "cloudflare";

        /// <summary>Only used when Provider == "custom". Must be a DoH template URL.</summary>
        public string? CustomTemplate { get; set; }

        public static readonly Dictionary<string, string> BuiltInProviders = new()
        {
            ["cloudflare"] = "https://dns.cloudflare.com/dns-query",
            ["google"] = "https://dns.google/dns-query",
            ["quad9"] = "https://dns.quad9.net/dns-query",
            ["cleanbrowsing"] = "https://doh.cleanbrowsing.org/doh/family-filter/",
        };
    }

    public class DownloadSettings
    {
        public string Location { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public bool AskWhereToSaveEachFile { get; set; } = false;
        public bool ShowDownloadsWhenDone { get; set; } = true;
    }

    public class NewTabBackgroundSettings
    {
        public NewTabBackgroundType Type { get; set; } = NewTabBackgroundType.None;

        /// <summary>Hex color, preset filename, or path under Backgrounds/ depending on Type.</summary>
        public string? Value { get; set; }

        public double OverlayOpacity { get; set; } = 0.3;
    }

    public class WindowSettings
    {
        public double Width { get; set; } = 1280;
        public double Height { get; set; } = 800;
        public bool IsMaximized { get; set; } = false;
    }

    /// <summary>
    /// Root of settings.json. Persisted via SettingsService. Keep this a plain
    /// data object (no logic) so it serializes cleanly with System.Text.Json.
    /// </summary>
    public class AppSettings
    {
        // General
        public string HomepageUrl { get; set; } = "about:newtab";
        public string DefaultSearchEngineName { get; set; } = "Google";
        public List<SearchEngine> SearchEngines { get; set; } = new(SearchEngine.Defaults);

        // Appearance
        public bool ShowBookmarkBar { get; set; } = false;
        public AppTheme Theme { get; set; } = AppTheme.System;
        public NewTabBackgroundSettings NewTabBackground { get; set; } = new();

        // Privacy & Security
        public SecureDnsSettings SecureDns { get; set; } = new();

        // Downloads
        public DownloadSettings Downloads { get; set; } = new();

        // Window state (restored on next launch)
        public WindowSettings Window { get; set; } = new();
    }
}
