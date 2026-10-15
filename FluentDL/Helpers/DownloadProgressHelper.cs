using FluentDL.Core.Sources;
using System.Diagnostics;
namespace FluentDL.Helpers;

internal class ProgressEventArgs(string displayString, double progressValue, bool preparing = false) : EventArgs
{
    public string DisplayString { get; } = displayString;
    public double ProgressValue { get; } = progressValue;

    /// <summary>No track has data yet (link and stream lookups). Show an indeterminate bar instead of 0%.</summary>
    public bool Preparing { get; } = preparing;
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
    // A track with no live byte progress (Deezer, yt-dlp, link lookups) never estimates past this share.
    private const double MaxEstimate = 0.9;

    private readonly string _title;
    private int _tracksCount;

    private readonly Dictionary<long, ProgressData> _progressDict = [];
    private readonly Dictionary<long, TimeSpan> _started = [];
    private readonly List<double> _completedSeconds = [];
    private readonly HashSet<long> _completed = [];
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastPercent;

    public event EventHandler<ProgressEventArgs>? ProgressUpdated;

    public DownloadProgressHelper(string title, int tracksCount = 1)
    {
        _title = title;
        _tracksCount = Math.Max(1, tracksCount);
    }

    public void SetTracksCount(int tracksCount)
    {
        lock (_progressDict) { _tracksCount = Math.Max(1, tracksCount); }
    }

    /// <summary>
    /// Never wider than "99.9%" with tabular digits, so the InfoBar doesn't grow late in a run: " 9.5%" starts with a
    /// figure space (as wide as a digit), and the end shows "100%", which is narrower than "100.0%".
    /// </summary>
    internal static string FormatPercent(double percent) => percent switch
    {
        >= 99.95 => "100%",
        < 9.95 => "\u2007" + Math.Max(percent, 0).ToString("0.0") + "%",
        _ => percent.ToString("0.0") + "%",
    };

    public string GenerateDisplayString(double percent, double? speedMBPS, string? status = null)
    {
        string percentDisplay = status ?? FormatPercent(percent);

        string? speedDisplay = speedMBPS != null
            ? (speedMBPS >= 100
                ? $"{(speedMBPS / 1024)?.ToString("N3")[..4]} GB/s"
                : $"{speedMBPS?.ToString("N2")[..4]} MB/s")
            : null;

        string? trackDisplay = _tracksCount > 1
            ? $"{_completed.Count.ToString($"D{_tracksCount.ToString().Length}")} / {_tracksCount}"
            : null;

        var parts = new List<string>
        {
            _title,
            $"{percentDisplay}{(trackDisplay != null ? $" ({trackDisplay})" : "")}"
        };

        if (speedDisplay != null)
            parts.Add(speedDisplay);
        return string.Join(" \u2022 ", parts);
    }

    public void UpdateProgress(ProgressData data)
    {
        UpdateProgressLoop(data);
        var update = GetProgressUpdate();
        if (update != null) ProgressUpdated?.Invoke(this, update);
    }

    private CancellationTokenSource? _cts;

    public void UpdateProgressLoop(ProgressData data)
    {
        lock (_progressDict)
        {
            var now = _stopwatch.Elapsed;
            _progressDict[data.uniqueId] = data;
            _started.TryAdd(data.uniqueId, now);
            if (Fraction(data) >= 1 && _completed.Add(data.uniqueId))
            {
                _completedSeconds.Add((now - _started[data.uniqueId]).TotalSeconds);
            }
        }
    }

    private static double Fraction(ProgressData data) =>
        data.percent is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    internal ProgressEventArgs? GetProgressUpdate(double? speedMBPS = null)
    {
        lock (_progressDict)
        {
            if (_progressDict.Count == 0) return null;
            var now = _stopwatch.Elapsed;
            var typicalSeconds = _completedSeconds.Count > 0 ? _completedSeconds.Average() : 0;
            double total = 0;
            foreach (var (id, data) in _progressDict)
            {
                var fraction = Fraction(data);
                // Without live progress, estimate from how long finished tracks took. Real reports replace it.
                if (fraction == 0 && typicalSeconds > 0)
                {
                    fraction = Math.Min(MaxEstimate, (now - _started[id]).TotalSeconds / typicalSeconds * MaxEstimate);
                }
                total += fraction;
            }
            // Each track has an equal share. Tracks that have not started count as zero.
            var percent = 100 * total / Math.Max(_tracksCount, _progressDict.Count);
            _lastPercent = Math.Min(100, Math.Max(_lastPercent, percent));
            if (_lastPercent == 0 && !_progressDict.Values.Any(d => d.currentBytes > 0))
            {
                return new ProgressEventArgs(GenerateDisplayString(0, null, "Preparing"), 0, preparing: true);
            }
            // The label and bar use the same value.
            return new ProgressEventArgs(GenerateDisplayString(_lastPercent, speedMBPS), _lastPercent / 100);
        }
    }

    public void StartLoop()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        var token = _cts.Token;
        var speedDamper = new ValueDamper(initialValue: 0.0, timeConstantSeconds: 2, maxDeltaPerSecond: 10.0);

        Task.Run(async () =>
        {
            try
            {
                long lastBytes = 0;
                var lastTime = _stopwatch.Elapsed;
                double shown = 0;
                // Run until the page stops the loop, so the final value is always shown.
                while (!token.IsCancellationRequested)
                {
                    long currentBytes;
                    lock (_progressDict)
                    {
                        currentBytes = _progressDict.Values.Sum(d => d.currentBytes ?? 0);
                    }
                    var now = _stopwatch.Elapsed;
                    var elapsed = (now - lastTime).TotalSeconds;
                    if (elapsed > 0)
                    {
                        speedDamper.Report(Math.Max((currentBytes - lastBytes) / elapsed / (1024 * 1024), 0));
                    }
                    lastBytes = currentBytes;
                    lastTime = now;
                    var speed = Math.Max(speedDamper.GetValue(), 0);
                    var update = GetProgressUpdate(speed);
                    if (update != null && !update.Preparing)
                    {
                        shown = Ease(shown, update.ProgressValue, elapsed);
                        update = new ProgressEventArgs(GenerateDisplayString(shown * 100, speed), shown);
                    }
                    if (update != null && !token.IsCancellationRequested)
                    {
                        ProgressUpdated?.Invoke(this, update);
                    }
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
        }, token);
    }

    /// <summary>
    /// Glides the shown value toward the measured one so bursts animate instead of jumping. It approaches from below
    /// and never passes the measured value; within 0.1 percentage point it snaps to it.
    /// </summary>
    internal static double Ease(double shown, double target, double seconds)
    {
        if (target <= shown) return shown;
        var next = shown + (target - shown) * (1 - Math.Exp(-Math.Max(seconds, 0) / EaseSeconds));
        return target - next < 0.001 ? target : Math.Min(next, target);
    }

    // About 95% of a jump is shown within three of these.
    private const double EaseSeconds = 0.3;

    /// <summary>False after StopLoop. Pages check it before painting a queued update over the result message.</summary>
    public bool IsRunning { get; private set; }

    public void StopLoop()
    {
        IsRunning = false;
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

    ~DownloadProgressHelper()
    {
        StopLoop();
    }
}

/// <summary>
/// One progress entry per track, whichever source or retry downloads it. It appears at 0% when the track starts, so
/// link lookups show progress. Source byte progress fills 95%; the rest is conversion and tagging. A finished track is
/// 100%, including a failed one, because the album or queue has processed it.
/// </summary>
internal sealed class TrackDownloadProgress : IProgress<ProgressData>
{
    private const double TransferShare = 0.95;
    private readonly IProgress<ProgressData> _progress;
    private readonly object _lock = new();
    private ProgressData _last = new() { uniqueId = ProgressData.NewId(), fraction = 0, currentBytes = 0 };
    private bool _finished;

    public TrackDownloadProgress(IProgress<ProgressData> progress)
    {
        _progress = progress;
        _progress.Report(_last);
    }

    public void Report(ProgressData value)
    {
        lock (_lock)
        {
            if (_finished) return;
            var fraction = value.percent is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) * TransferShare : 0;
            value.uniqueId = _last.uniqueId;
            value.fraction = Math.Max(fraction, _last.fraction ?? 0);
            _last = value;
            _progress.Report(value);
        }
    }

    public void Finish()
    {
        lock (_lock)
        {
            if (_finished) return;
            _finished = true;
            _last.fraction = 1;
            _last.bps = 0;
            _progress.Report(_last);
        }
    }
}
