using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using FluentDL.Core.Sources;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace FluentDL.Helpers;

// One decoded image per cover URL while queue rows own it. Encoded bytes live only during loading and persistence.
internal sealed class QueueArtworkLoader
{
    private sealed class Entry(string url, BitmapImage? image)
    {
        internal readonly string Url = url;
        internal readonly HashSet<QueueObject> Rows = new(ReferenceEqualityComparer.Instance);
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal BitmapImage? Image = image;
        internal bool Published;
        internal bool Failed;
    }

    private readonly ObservableCollection<QueueObject> source;
    private readonly DispatcherQueue dispatcher;
    private readonly BitmapImage placeholder;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Channel<Entry> pending = Channel.CreateUnbounded<Entry>();
    private readonly TaskCompletionSource networkReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HttpClient http = new() { MaxResponseContentBufferSize = ArtworkThumbnail.MaximumEncodedBytes, Timeout = TimeSpan.FromSeconds(15) };
    private readonly Task[] workers;
    private volatile bool stopped;
    internal event Action<string>? ArtworkChanged;

    internal QueueArtworkLoader(ObservableCollection<QueueObject> source, DispatcherQueue dispatcher, BitmapImage placeholder)
    {
        this.source = source;
        this.dispatcher = dispatcher;
        this.placeholder = placeholder;
        source.CollectionChanged += Changed;
        workers = Enumerable.Range(0, 4).Select(_ => Task.Run(ConsumeAsync)).ToArray();
    }

    internal void StartNetwork() => networkReady.TrySetResult();
    internal int EntryCount => entries.Count;
    internal Task<bool>? CompletionFor(QueueObject row) => entries.TryGetValue(row.ImageLocation, out var entry) ? entry.Completion.Task : null;
    internal bool TryGet(string url, out BitmapImage? image, out QueueArtworkState state)
    {
        image = null;
        state = QueueArtworkState.Unavailable;
        if (!entries.TryGetValue(url, out var entry)) return false;
        image = entry.Image;
        state = image != null ? QueueArtworkState.Ready : entry.Failed ? QueueArtworkState.Unavailable : QueueArtworkState.Loading;
        return true;
    }

    internal Task StopAsync()
    {
        if (!stopped)
        {
            stopped = true;
            source.CollectionChanged -= Changed;
            foreach (var entry in entries.Values)
            {
                entry.Cancellation.Cancel();
                entry.Rows.Clear();
                entry.Image = null;
            }
            entries.Clear();
            pending.Writer.TryComplete();
            http.CancelPendingRequests();
        }
        return Task.WhenAll(workers);
    }

    private void Changed(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Move) return;
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in entries.Values)
            {
                entry.Cancellation.Cancel();
                entry.Rows.Clear();
                entry.Image = null;
            }
            entries.Clear();
        }
        if (args.OldItems != null)
            foreach (QueueObject row in args.OldItems)
                if (entries.TryGetValue(row.ImageLocation, out var entry) && entry.Rows.Remove(row) && entry.Rows.Count == 0)
                {
                    entries.Remove(entry.Url);
                    entry.Cancellation.Cancel();
                    entry.Image = null;
                }
        if (args.NewItems == null || args.Action == NotifyCollectionChangedAction.Move) return;
        foreach (QueueObject row in args.NewItems)
        {
            if (row.Source == SourceIds.Local || !Uri.TryCreate(row.ImageLocation, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")) continue;
            row.LocalBitmapImage ??= placeholder;
            if (!entries.TryGetValue(row.ImageLocation, out var entry))
            {
                entry = new(row.ImageLocation, ReferenceEquals(row.LocalBitmapImage, placeholder) ? null : row.LocalBitmapImage as BitmapImage);
                entries.Add(entry.Url, entry);
                entry.Rows.Add(row);
                row.ArtworkState = entry.Image != null ? QueueArtworkState.Ready : QueueArtworkState.Loading;
                pending.Writer.TryWrite(entry);
                ArtworkChanged?.Invoke(entry.Url);
            }
            else
            {
                entry.Rows.Add(row);
                if (entry.Image != null)
                {
                    row.LocalBitmapImage = entry.Image;
                    row.ArtworkState = QueueArtworkState.Ready;
                }
                else row.ArtworkState = entry.Completion.Task.IsCompleted ? QueueArtworkState.Unavailable : QueueArtworkState.Loading;
                if (entry.Published)
                {
                    row.LocalBitmapImage = entry.Image;
                    _ = ObserveAsync(DatabaseService.SaveArtworkAsync(QueueViewModel.GetHash(row), entry.Url, null));
                }
            }
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var entry in pending.Reader.ReadAllAsync())
        {
            var success = false;
            byte[]? bytes = null;
            IDisposable? activity = null;
            try
            {
                var token = entry.Cancellation.Token;
                token.ThrowIfCancellationRequested();
                // Album expansion inserts rows under an exclusive gate. Artwork starts after that edit commits.
                while (ApplicationActivity.Gate.IsExclusiveWork) await Task.Delay(10, token);
                activity = ApplicationActivity.Gate.EnterWork();
                bytes = await DatabaseService.LoadArtworkByUrlAsync(entry.Url);
                if (bytes == null)
                {
                    // Missing covers never hold the splash screen or cause cached covers to redownload.
                    await networkReady.Task.WaitAsync(token);
                    using var stream = await ArtworkThumbnail.DownloadAsync(http, entry.Url, token);
                    bytes = await ArtworkThumbnail.CreateJpegAsync(stream, 76, token);
                }
                token.ThrowIfCancellationRequested();
                var saves = await OnUiAsync(async () =>
                {
                    if (!entries.TryGetValue(entry.Url, out var current) || !ReferenceEquals(current, entry)) return Array.Empty<Task>();
                    var image = entry.Image;
                    if (image == null)
                    {
                        using var stream = new InMemoryRandomAccessStream();
                        await stream.WriteAsync(bytes.AsBuffer());
                        stream.Seek(0);
                        token.ThrowIfCancellationRequested();
                        image = new BitmapImage { DecodePixelHeight = 76 };
                        await image.SetSourceAsync(stream).AsTask(token);
                    }
                    token.ThrowIfCancellationRequested();
                    var writes = entry.Rows.Select(row => DatabaseService.SaveArtworkAsync(QueueViewModel.GetHash(row), entry.Url, bytes)).ToArray();
                    entry.Image = image;
                    entry.Published = true;
                    foreach (var row in entry.Rows)
                    {
                        row.LocalBitmapImage = image;
                        row.ArtworkState = QueueArtworkState.Ready;
                    }
                    ArtworkChanged?.Invoke(entry.Url);
                    return writes;
                }, token);
                await Task.WhenAll(saves);
                success = !token.IsCancellationRequested;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Serilog.Log.Debug("Queue cover could not be cached: {ExceptionType}", error.GetType().Name); }
            finally
            {
                if (!success && !stopped && !entry.Cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await OnUiAsync(() =>
                        {
                            entry.Failed = true;
                            foreach (var row in entry.Rows)
                                if (row.ArtworkState == QueueArtworkState.Loading) row.ArtworkState = QueueArtworkState.Unavailable;
                            ArtworkChanged?.Invoke(entry.Url);
                            return Task.FromResult(true);
                        }, entry.Cancellation.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { Serilog.Log.Debug("Queue cover status could not be updated: {ExceptionType}", error.GetType().Name); }
                }
                bytes = null;
                entry.Completion.TrySetResult(success);
                activity?.Dispose();
            }
        }
    }

    private async Task<T> OnUiAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            if (stopped || token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { completion.TrySetResult(await action()); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetCanceled();
        return await completion.Task;
    }

    private static async Task ObserveAsync(Task save)
    {
        try { await save; }
        catch (Exception error) { Serilog.Log.Error(error, "Shared queue cover could not be saved"); }
    }
}
