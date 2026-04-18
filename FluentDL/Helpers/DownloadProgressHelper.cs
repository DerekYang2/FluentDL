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

internal class ValueDamper
{
    private readonly object _lock = new object();

    // tuning parameters
    public double TimeConstantSeconds { get; }
    public double MaxDeltaPerSecond { get; } // maximum allowed change per second (absolute)

    // state
    private double _current;
    private double _target;
    private DateTime _lastUpdateUtc;

    public ValueDamper(double initialValue = 0.0, double timeConstantSeconds = 0.5, double maxDeltaPerSecond = double.PositiveInfinity)
    {
        TimeConstantSeconds = Math.Max(1e-6, timeConstantSeconds);
        MaxDeltaPerSecond = maxDeltaPerSecond;
        _current = initialValue;
        _target = initialValue;
        _lastUpdateUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Report a new raw measurement (target). Does not immediately set the displayed value;
    /// the displayed value will ease toward this target when GetValue is called.
    /// </summary>
    public void Report(double newValue)
    {
        lock (_lock)
        {
            _target = newValue;
            // keep lastUpdate time as-is; smoothing happens in GetValue
        }
    }

    /// <summary>
    /// Force both current and target to a value (instant jump).
    /// </summary>
    public void ForceSet(double value)
    {
        lock (_lock)
        {
            _current = value;
            _target = value;
            _lastUpdateUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Reset the damper to a value and reset internal timer.
    /// </summary>
    public void Reset(double value = 0.0)
    {
        ForceSet(value);
    }

    /// <summary>
    /// Returns the smoothed value, advancing internal state using the current time.
    /// Optionally pass a custom 'now' for deterministic updates (useful with Stopwatch).
    /// </summary>
    public double GetValue(DateTime? nowUtc = null)
    {
        lock (_lock)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            var dt = (now - _lastUpdateUtc).TotalSeconds;
            if (dt <= 0)
            {
                return _current;
            }

            // exponential smoothing alpha from time constant tau
            var tau = TimeConstantSeconds;
            var alpha = 1.0 - Math.Exp(-dt / tau);

            // candidate eased value
            var eased = _current + (_target - _current) * alpha;

            // clamp the change to MaxDeltaPerSecond * dt to avoid large instantaneous jumps
            if (!double.IsInfinity(MaxDeltaPerSecond))
            {
                var maxDelta = MaxDeltaPerSecond * dt;
                var delta = eased - _current;
                if (Math.Abs(delta) > maxDelta)
                {
                    eased = _current + Math.Sign(delta) * maxDelta;
                }
            }

            _current = eased;
            _lastUpdateUtc = now;
            return _current;
        }
    }

    /// <summary>
    /// Convenience: check whether the displayed value is effectively at the target.
    /// </summary>
    public bool IsAtTarget(double epsilon = 1e-6)
    {
        lock (_lock)
        {
            return Math.Abs(_current - _target) <= epsilon;
        }
    }

    /// <summary>
    /// Peek the current target (not smoothed).
    /// </summary>
    public double PeekTarget()
    {
        lock (_lock)
        {
            return _target;
        }
    }
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
    private long lastBytesCycle = 0;
    private TimeSpan lastTimeCycle = TimeSpan.Zero;

    public void UpdateProgressLoop(ProgressData data)
    {
        // synchronous; no async void
        lock (_progressDict)
        {
            _progressDict[data.uniqueId] = data;

            var sumMBPS = _progressDict.Values.Sum(d => d.MBPS());

            // _speedQueue.AddLast(sumMBPS);
            //if (_speedQueue.Count > 50) _speedQueue.RemoveFirst();

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

        var percentDamper = new ValueDamper(initialValue: 0.0, timeConstantSeconds: 1, maxDeltaPerSecond: 10.0);
        var speedDamper = new ValueDamper(initialValue: 0.0, timeConstantSeconds: 2, maxDeltaPerSecond: 10.0);

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

                            // Speed calculations
                            double cycleTime = (curTime - lastTimeCycle).TotalSeconds;
                            if (cycleTime > 0.05)
                            {
                                var mbps = Math.Max((curBytes - lastBytesCycle) / (curTime - lastTimeCycle).TotalSeconds / (1024 * 1024), 0);
                                lastBytesCycle = curBytes;
                                lastTimeCycle = curTime;
                                speedDamper.Report(mbps);
                            }

                            // Report values
                            percentDamper.Report(percent);

                            var now = DateTime.UtcNow;
                            var displayPercent = percentDamper.GetValue(now);
                            var displaySpeed = speedDamper.GetValue(now);

                            display = GenerateDisplayString(displayPercent, Math.Max(displaySpeed, 0));
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