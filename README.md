# Loomis Browser

A small, general-purpose Windows web browser built on **WebView2** (Chromium),
with tabs, bookmarks, history, private browsing, and a self-updating installer.
Open source under the MIT License.

Repo: https://github.com/vinhdubaii/loomis-browser

## Status

This is an early skeleton — architecture and core plumbing are in place, but
several pieces are intentionally left as follow-ups (see "Known gaps" below).
`src/Assets/` is empty on purpose; drop your icons in before building a
release (see below).

## Stack

- **C# / WPF** (`net8.0-windows`), custom Epiphany-style unified title bar
  via `WindowChrome`
- **Microsoft.Web.WebView2** — one `CoreWebView2Environment` for normal
  browsing (persistent profile in `%AppData%\Loomis Browser\WebView2Profile`),
  a second, temp-folder environment for Private windows
- **Microsoft.Data.Sqlite** — `history` and `bookmarks` tables in
  `%AppData%\Loomis Browser\browser.db`
- **System.Text.Json** — `settings.json` in the same folder (search engine,
  Secure DNS, downloads, appearance, new-tab background, window state)

## Project layout

```
src/
  App.xaml(.cs)            App-lifetime services wiring (Settings/History/Bookmarks/...)
  MainWindow.xaml(.cs)      Normal browser window: toolbar, tab strip, bookmark bar, content host
  Models/                   Plain data types (BrowserTab, BookmarkItem, HistoryItem, ...)
  Services/                 SettingsService, HistoryService, BookmarkService,
                             WebViewEnvironmentService, SearchEngineService,
                             DownloadService, UpdateService
  Views/                    NewTabPage, LibraryPanel, SettingsWindow,
                             PrivateWindow, BackgroundPickerWindow
  Assets/                   App icon / logo / new-tab backgrounds (empty — add your own)
installer/
  setup.iss                 Inno Setup script (AppId is fixed — do not change it)
.github/workflows/
  build.yml                 CI: restore + build on every push/PR
  release.yml               On tag `vX.Y.Z`: publish, compile installer, upload to GitHub Release
```

## Adding your icon

1. Design a square logo at 512×512 or larger (you already have a 2000×2000 PNG — plenty).
2. Generate a multi-size `.ico` (16/32/48/64/128/256):
   ```
   magick convert loomis-logo-2000.png -define icon:auto-resize=256,128,64,48,32,16 loomis.ico
   ```
3. Place it at `src/Assets/loomis.ico`. The `.csproj` picks it up automatically
   (`ApplicationIcon` is conditioned on the file existing) — no edits needed.
4. Optionally uncomment `SetupIconFile=` in `installer/setup.iss` once the icon exists.

## Building locally

Requires the .NET 8 SDK and Windows (WebView2/WPF are Windows-only).

```
dotnet restore src/LoomisBrowser.csproj
dotnet build src/LoomisBrowser.csproj -c Release
dotnet run --project src/LoomisBrowser.csproj
```

## Releasing

Push a tag matching `v*.*.*` (e.g. `v0.1.0`). `release.yml` will:
1. `dotnet publish` a self-contained `win-x64` build
2. Compile `installer/setup.iss` with Inno Setup, versioned from the tag
3. Upload `LoomisBrowser-Setup-<version>.exe` to a new GitHub Release

`UpdateService` polls `GET /repos/vinhdubaii/loomis-browser/releases/latest`
and, when a newer tag is found, can download and silently run that installer
(`/VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`).

## Known gaps / next steps

- **Search engine management UI** (`SettingsWindow.ManageEnginesButton_Click`)
  is a placeholder — adding/removing custom engines needs a small dialog.
- **Top Sites "Remove"** context menu item is wired up visually but not
  functional yet — needs a "hidden sites" list in `HistoryService`.
- **Find in Page** menu item is stubbed — needs a small find bar over the
  WebView2 content plus `CoreWebView2.Find` (or the standard workaround via
  injected script, depending on the WebView2 SDK version in use).
- **Update notification banner** — `UpdateService.UpdateAvailable` fires but
  `MainWindow` doesn't yet show the "A new version is available" banner UI.
- **Tab drag-to-reorder** isn't implemented; tabs are click-to-activate only.
- Only one tab per Private window for now (kept intentionally simple, per
  earlier scoping) — multi-tab private windows are a possible follow-up.
