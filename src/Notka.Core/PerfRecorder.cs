using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Notka.Core
{
    public sealed class PerfRecorder
    {
        private const int MaxSamples = 4000;
        private static readonly TimeSpan SummaryEvery = TimeSpan.FromSeconds(60);

        private readonly Action<string> _write;
        private readonly double _slowMs;
        private readonly TimeProvider _time;
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly object _gate = new();
        private Dictionary<string, List<double>> _window = [];
        private DateTimeOffset _windowStart;

        public PerfRecorder(Action<string> write, TimeSpan? slow = null, TimeProvider? time = null)
        {
            _write = write;
            _slowMs = (slow ?? TimeSpan.FromMilliseconds(250)).TotalMilliseconds;
            _time = time ?? TimeProvider.System;
            _windowStart = _time.GetUtcNow();
        }

        public Scope Measure(string name) => new(this, name, Stopwatch.GetTimestamp());

        public void Record(string name, double milliseconds)
        {
            lock (_gate)
            {
                if (!_window.TryGetValue(name, out var samples))
                {
                    samples = [];
                    _window[name] = samples;
                }

                if (samples.Count < MaxSamples)
                {
                    samples.Add(milliseconds);
                }
            }

            if (milliseconds >= _slowMs)
            {
                _lines.Enqueue($"slow op=\"{name}\" ms={Format(milliseconds)} at={At()}");
            }
        }

        public void Event(string name, double milliseconds) =>
            _lines.Enqueue($"event name=\"{name}\" ms={Format(milliseconds)} at={At()}");

        private string At() => _time.GetLocalNow().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        public void Flush(bool force = false)
        {
            try
            {
                while (_lines.TryDequeue(out var line))
                {
                    _write(line);
                }

                var now = _time.GetUtcNow();
                if (!force && now - _windowStart < SummaryEvery)
                {
                    return;
                }

                Dictionary<string, List<double>> window;
                lock (_gate)
                {
                    window = _window;
                    _window = [];
                    _windowStart = now;
                }

                foreach (var (name, samples) in window.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    samples.Sort();
                    var p95 = samples[Math.Max(0, (int)Math.Ceiling(samples.Count * 0.95) - 1)];
                    _write($"op=\"{name}\" n={samples.Count} avg={Format(samples.Average())} p95={Format(p95)} max={Format(samples[^1])}");
                }
            }
            catch (Exception)
            {
            }
        }

        private static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        public readonly struct Scope(PerfRecorder? recorder, string name, long start) : IDisposable
        {
            public void Dispose() => recorder?.Record(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public static class Perf
    {
        public static PerfRecorder? Recorder { get; set; }

        public static PerfRecorder.Scope Measure(string name) =>
            Recorder is { } recorder ? recorder.Measure(name) : default;
    }
}
