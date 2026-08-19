namespace RemiBrowser.Models
{
    public class SearchEngine
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>URL template containing a %s placeholder for the URL-encoded query.</summary>
        public string UrlTemplate { get; set; } = string.Empty;

        /// <summary>Short prefix for quick-switch in the address bar, e.g. "g" for !g query.</summary>
        public string Shortcut { get; set; } = string.Empty;

        public bool IsBuiltIn { get; set; } = true;

        /// <summary>
        /// The custom ComboBox ControlTemplate in ControlStyles.xaml renders the closed
        /// selection box via ContentPresenter bound to SelectionBoxItem, without going
        /// through DisplayMemberPath. Overriding ToString() ensures the closed box shows
        /// the engine's name instead of falling back to the fully-qualified type name.
        /// </summary>
        public override string ToString() => Name;

        public static SearchEngine[] Defaults => new[]
        {
            new SearchEngine { Name = "Google",     UrlTemplate = "https://www.google.com/search?q=%s",   Shortcut = "g" },
            new SearchEngine { Name = "Bing",       UrlTemplate = "https://www.bing.com/search?q=%s",     Shortcut = "b" },
            new SearchEngine { Name = "DuckDuckGo", UrlTemplate = "https://duckduckgo.com/?q=%s",         Shortcut = "d" },
            new SearchEngine { Name = "Cốc Cốc",    UrlTemplate = "https://coccoc.com/search?query=%s",   Shortcut = "c" },
        };
    }
}
