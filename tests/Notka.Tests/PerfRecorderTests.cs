using Notka.Core;

namespace Notka.Tests
{
    public sealed class PerfRecorderTests
    {
        private sealed class ManualTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }

        [Fact]
        public void A_summary_reports_count_average_p95_and_max_per_operation_once_a_minute()
        {
            var lines = new List<string>();
            var time = new ManualTime();
            var recorder = new PerfRecorder(lines.Add, time: time);
            for (var i = 1; i <= 20; i++)
            {
                recorder.Record("api GET /api/tasks", i);
            }

            recorder.Record("ui.tabSwitch", 5);
            recorder.Flush();
            Assert.Empty(lines);

            time.Now += TimeSpan.FromSeconds(61);
            recorder.Flush();

            Assert.Equal(
                ["op=\"api GET /api/tasks\" n=20 avg=10.5 p95=19.0 max=20.0", "op=\"ui.tabSwitch\" n=1 avg=5.0 p95=5.0 max=5.0"],
                lines);
        }

        [Fact]
        public void Each_window_starts_empty_and_operations_without_samples_are_skipped()
        {
            var lines = new List<string>();
            var time = new ManualTime();
            var recorder = new PerfRecorder(lines.Add, time: time);
            recorder.Record("a", 1);
            time.Now += TimeSpan.FromSeconds(61);
            recorder.Flush();
            lines.Clear();

            time.Now += TimeSpan.FromSeconds(61);
            recorder.Flush();

            Assert.Empty(lines);
        }

        [Fact]
        public void A_slow_operation_is_written_on_the_next_flush_not_when_it_is_recorded()
        {
            var lines = new List<string>();
            var recorder = new PerfRecorder(lines.Add, TimeSpan.FromMilliseconds(100));

            recorder.Record("ui.settingsOpen", 99);
            recorder.Record("ui.settingsOpen", 150);
            Assert.Empty(lines);

            recorder.Flush();

            var line = Assert.Single(lines);
            Assert.StartsWith("slow op=\"ui.settingsOpen\" ms=150.0 at=", line);
        }

        [Fact]
        public void Startup_events_are_always_written()
        {
            var lines = new List<string>();
            var recorder = new PerfRecorder(lines.Add);

            recorder.Event("startup.api", 420.26);
            recorder.Flush();

            var line = Assert.Single(lines);
            Assert.StartsWith("event name=\"startup.api\" ms=420.3 at=", line);
        }

        [Fact]
        public void A_failing_writer_never_throws_into_the_caller()
        {
            var recorder = new PerfRecorder(_ => throw new IOException("disk full"));
            recorder.Event("x", 1);

            recorder.Flush(force: true);
        }

        [Fact]
        public void A_scope_records_its_duration_and_a_missing_recorder_is_a_no_op()
        {
            var lines = new List<string>();
            var recorder = new PerfRecorder(lines.Add);
            using (recorder.Measure("work"))
            {
            }

            recorder.Flush(force: true);
            Assert.Single(lines);
            Assert.StartsWith("op=\"work\" n=1", lines[0]);

            Perf.Recorder = null;
            using (Perf.Measure("nothing"))
            {
            }
        }

        [Theory]
        [InlineData("/api/tasks/17/complete", "/api/tasks/{id}/complete")]
        [InlineData("/api/notes/3", "/api/notes/{id}")]
        [InlineData("/api/tasks", "/api/tasks")]
        [InlineData("/api/tasks/", "/api/tasks/")]
        public void The_http_handler_names_requests_without_ids(string path, string expected)
        {
            Assert.Equal(expected, Notka.Desktop.Services.PerfHandler.Normalize(path));
        }

        [Fact]
        public async Task Recording_from_many_threads_loses_nothing_below_the_cap()
        {
            var lines = new List<string>();
            var recorder = new PerfRecorder(lines.Add);

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 400; i++)
                {
                    recorder.Record("t", 1);
                }
            }, TestContext.Current.CancellationToken)));
            recorder.Flush(force: true);

            Assert.Contains("n=3200", lines.Single());
        }
    }
}
