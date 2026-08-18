using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Web.WebView2.Core;
using LoomisBrowser.Models;

namespace LoomisBrowser.Services
{
    /// <summary>
    /// Tracks in-flight and completed downloads across all tabs/windows.
    /// Call Attach(coreWebView2) once per WebView2 instance (normal or private)
    /// right after CoreWebView2InitializationCompleted fires.
    /// </summary>
    public class DownloadService
    {
        private readonly SettingsService _settings;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        public event EventHandler<DownloadItem>? DownloadCompleted;

        public DownloadService(SettingsService settings)
        {
            _settings = settings;
        }

        public void Attach(CoreWebView2 coreWebView2)
        {
            coreWebView2.DownloadStarting += OnDownloadStarting;
        }

        private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var settings = _settings.Current.Downloads;

            if (settings.AskWhereToSaveEachFile)
            {
                // WebView2 will show its own native "Save As" dialog when we don't
                // override ResultFilePath and leave Cancel = false / Handled = false.
                // Nothing further to do here; we still track it below via the item.
            }
            else
            {
                var fileName = Path.GetFileName(e.ResultFilePath);
                Directory.CreateDirectory(settings.Location);
                e.ResultFilePath = Path.Combine(settings.Location, fileName);
            }

            var item = new DownloadItem
            {
                FileName = Path.GetFileName(e.ResultFilePath),
                FilePath = e.ResultFilePath,
                Url = e.DownloadOperation.Uri,
                TotalBytes = (ulong)Math.Max(0, e.DownloadOperation.TotalBytesToReceive ?? 0)
            };

            App.Current.Dispatcher.Invoke(() => Downloads.Insert(0, item));

            e.DownloadOperation.BytesReceivedChanged += (_, _) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                    item.ReceivedBytes = (ulong)e.DownloadOperation.BytesReceived);
            };

            e.DownloadOperation.StateChanged += (_, _) =>
            {
                var state = e.DownloadOperation.State switch
                {
                    CoreWebView2DownloadState.InProgress => DownloadState.InProgress,
                    CoreWebView2DownloadState.Interrupted => DownloadState.Failed,
                    CoreWebView2DownloadState.Completed => DownloadState.Completed,
                    _ => DownloadState.InProgress
                };

                App.Current.Dispatcher.Invoke(() =>
                {
                    item.State = state;
                    if (state == DownloadState.Completed)
                        DownloadCompleted?.Invoke(this, item);
                });
            };
        }
    }
}
