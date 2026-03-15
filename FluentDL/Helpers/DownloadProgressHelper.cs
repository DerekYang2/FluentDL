using FFMpegCore.Enums;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using Windows.Media.PlayTo;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace FluentDL.Helpers;

internal class ProgressEventArgs(string displayString, double progressValue) : EventArgs
{
    public string DisplayString { get; } = displayString;
    public double ProgressValue { get; } = progressValue;
}

internal class DownloadProgressHelper
{
    private readonly string _title;
    private int _tracksCount;

    private readonly Dictionary<long, ProgressData> _progressDict = [];
    private readonly LinkedList<double> _speedQueue = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastReport;

    public event EventHandler<ProgressEventArgs>? ProgressUpdated;

    public DownloadProgressHelper(
        string title,
        int tracksCount = 1
    )
    {
        _title = title;
        _tracksCount = tracksCount;
        _lastReport = _stopwatch.Elapsed;
    }

    public void SetTracksCount(int tracksCount) { _tracksCount = tracksCount; }

    public string GenerateDisplayString(double percent, double? speedMBPS)
    {
        string percentDisplay = Math.Round(percent, 2).ToString("N2")[..4];
        if (percentDisplay.EndsWith('.'))
            percentDisplay = percentDisplay[..^1];

        string? speedDisplay = speedMBPS != null
            ? (speedMBPS >= 100
                ? $"{(speedMBPS / 1024)?.ToString("N3")[..4]} GB/s"
                : $"{speedMBPS?.ToString("N2")[..4]} MB/s")
            : null;

        var downloadCount = _progressDict.Count;
        string? trackDisplay = _tracksCount > 1
            ? $"{downloadCount.ToString($"D{_tracksCount.ToString().Length}")} / {_tracksCount}"
            : null;

        var parts = new List<string>
                        {
                            _title,
                            $"{percentDisplay}%{(trackDisplay != null ? $" ({trackDisplay})" : "")}"
                        };

        if (speedDisplay != null)
            parts.Add(speedDisplay);
        return string.Join(" \u2022 ", parts);
    }

    public void UpdateProgress(ProgressData data)
    {
        lock (_progressDict)
        {
            _progressDict[data.uniqueId] = data;
            var sumMBPS = _progressDict.Values.Sum(d => d.MBPS());
            _speedQueue.AddLast(sumMBPS);
            if (_speedQueue.Count > 50) _speedQueue.RemoveFirst();

            if ((_stopwatch.Elapsed - _lastReport).TotalMilliseconds > 25)
            {
                _lastReport = _stopwatch.Elapsed;

                var averageBytes = _progressDict.Values.Average(d => d.totalBytes);
                if (averageBytes != null)
                {
                    var totalBytes = averageBytes * _tracksCount;
                    var currentBytes = _progressDict.Values.Sum(d => d.currentBytes ?? 0);

                    if (totalBytes != null)
                    {
                        var percent = 100 * currentBytes / (double)totalBytes;
                        var finalStr = GenerateDisplayString(percent, data.bps != null ? _speedQueue.Average() : null);
                        ProgressUpdated?.Invoke(this, new ProgressEventArgs(finalStr, percent / 100.0));
                    }
                }
            }
        }
    }


    private CancellationTokenSource? _cts;
    private long lastBytesPredicted = 0;
    private TimeSpan lastTimePredicted = TimeSpan.Zero;

    public void UpdateProgressLoop(ProgressData data)
    {
        // synchronous; no async void
        lock (_progressDict)
        {
            _progressDict[data.uniqueId] = data;

            var sumMBPS = _progressDict.Values.Sum(d => d.MBPS());
            _speedQueue.AddLast(sumMBPS);
            if (_speedQueue.Count > 50) _speedQueue.RemoveFirst();


            var currentBytes = _progressDict.Values.Sum(d => d.currentBytes ?? 0L);
            var currentTime = _stopwatch.Elapsed.TotalMilliseconds;
            if (currentTime > 0.0)
            {
                lastBytesPredicted = currentBytes;
                lastTimePredicted = _stopwatch.Elapsed;
            }
            // else: skip update to avoid divide by zero
        }
    }

    public void StartLoop()
    {
        // Cancel any existing loop
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _stopwatch.Start();
        // Initial times
        lastTimePredicted = _stopwatch.Elapsed;

        Task.Run(async () =>
        {
            try
            {
                double percentMax = 0;
                while (!token.IsCancellationRequested)
                {
                    double percent = 0.0;
                    string display = string.Empty;

                    lock (_progressDict)
                    {

                        var curTime = _stopwatch.Elapsed;
                        var deltaMs = (curTime - lastTimePredicted).TotalMilliseconds;
                        var curBytes = lastBytesPredicted;
                        // compute average totalBytes safely
                        var totals = _progressDict.Values.Select(d => d.totalBytes).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                        if (totals.Count > 0 && _tracksCount > 0)
                        {
                            var averageBytes = totals.Average();
                            var totalBytes = averageBytes * _tracksCount;

                            // How much bytes would have increased by
                            if (_progressDict.Count > 3 && _tracksCount > 5)  // Only use prediction after enough data
                            {
                                var slowdownFactor = Math.Min((double)(totalBytes - curBytes) / (totalBytes * 0.15), 1);  // Triggers when only 15% left to go
                                curBytes += (long)((curBytes / curTime.TotalMilliseconds) * deltaMs * slowdownFactor);
                            }
                            percent = Math.Min(100.0 * curBytes / (double)totalBytes, 100.0);
       
                            percentMax = Math.Max(percentMax, percent);  // This value should never decrease
                            var avgMBPS = _speedQueue.Average();
                            display = GenerateDisplayString(percent, avgMBPS > 0 ? avgMBPS : null);
                        }

                        lastTimePredicted = curTime;
                        lastBytesPredicted = curBytes;
                    }

                    if (percentMax >= 100.0)
                    {
                        break;
                    }

                    await Task.Delay(100, token).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(display) && !token.IsCancellationRequested)
                    {

                        ProgressUpdated?.Invoke(this, new ProgressEventArgs(display, percentMax / 100.0));
                    }

                }
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
        }, token);
    }

    public void StopLoop()
    {
        try
        {
            _cts?.Cancel();
            _cts = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping progress loop: {ex}");
        }
    }

    // Destructor
    ~DownloadProgressHelper()
    {
        StopLoop();
    }
}