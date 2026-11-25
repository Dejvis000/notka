using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Notka.Core;
using Serilog;

namespace Notka.Desktop
{
    internal static class PerfLog
    {
        private const int FlushSeconds = 5;
        private const int ProcessLineEvery = 12;

        private static readonly List<(string Name, double Milliseconds)> Early = [];
        private static long s_main;
        private static double s_beforeMain;
        private static Serilog.Core.Logger? s_logger;
        private static Timer? s_timer;
        private static int s_ticks;
        private static TimeSpan s_cpu;
        private static long s_wall;

        public static void MarkMain()
        {
            s_main = Stopwatch.GetTimestamp();
            try
            {
                s_beforeMain = Math.Max(0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        public static void Mark(string name)
        {
            var milliseconds = Stopwatch.GetElapsedTime(s_main).TotalMilliseconds;
            if (Perf.Recorder is { } recorder)
            {
                recorder.Event($"startup.{name}", milliseconds);
            }
            else
            {
                Early.Add((name, milliseconds));
            }
        }

        public static void Start()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "logs", "perf-.log");
                s_logger = new LoggerConfiguration()
                    .WriteTo.File(path, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message:lj}{NewLine}", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
                    .CreateLogger();
                var recorder = new PerfRecorder(line => s_logger?.Information("{Line:l}", line));
                Perf.Recorder = recorder;
                recorder.Event("startup.beforeMain", s_beforeMain);
                foreach (var (name, milliseconds) in Early)
                {
                    recorder.Event($"startup.{name}", milliseconds);
                }

                Early.Clear();
                s_cpu = Process.GetCurrentProcess().TotalProcessorTime;
                s_wall = Stopwatch.GetTimestamp();
                s_timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(FlushSeconds), TimeSpan.FromSeconds(FlushSeconds));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Performance log is unavailable");
            }
        }

        public static void Stop()
        {
            s_timer?.Dispose();
            s_timer = null;
            Perf.Recorder?.Flush(force: true);
            s_logger?.Dispose();
            s_logger = null;
        }

        private static void Tick()
        {
            try
            {
                Perf.Recorder?.Flush();
                if (++s_ticks % ProcessLineEvery != 0 || s_logger is null)
                {
                    return;
                }

                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                var elapsed = Stopwatch.GetElapsedTime(s_wall);
                var share = (cpu - s_cpu).TotalMilliseconds / Math.Max(1, elapsed.TotalMilliseconds) * 100;
                s_cpu = cpu;
                s_wall = Stopwatch.GetTimestamp();
                s_logger.Information(
                    "proc cpu={Cpu:0.0} ws={Memory:0} threads={Threads} gc0={Gc0} gc1={Gc1} gc2={Gc2}",
                    share,
                    process.WorkingSet64 / 1048576.0,
                    process.Threads.Count,
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2));
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class UiPerf
    {
        public static void UntilIdle(string name, DispatcherQueue? queue)
        {
            var start = Stopwatch.GetTimestamp();
            queue?.TryEnqueue(DispatcherQueuePriority.Low, () => Perf.Recorder?.Record(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        }
    }
}
