using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace TaskManager.Diagnostics
{
    public class SpanRecord
    {
        public string TraceId { get; set; } = "";
        public string SpanId { get; set; } = "";
        public string? ParentSpanId { get; set; }
        public string Name { get; set; } = "";
        public DateTime Start { get; set; }
        public double DurationMs { get; set; }
        public string Outcome { get; set; } = "";
        public string? Details { get; set; }
        public List<string> Steps { get; set; } = new();
        public Dictionary<string, string?> Tags { get; set; } = new();
    }

    // собирает завершенные операции (Activity) для отчета и пишет их в logs/traces-ГГГГММДД.json
    public sealed class TraceCollector : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly StreamWriter _writer;
        private readonly object _lock = new();
        private readonly List<SpanRecord> _spans = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public string FilePath { get; }

        public TraceCollector(string folder)
        {
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, $"traces-{DateTime.Now:yyyyMMdd}.json");
            _writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };

            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == AppTracing.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = OnStopped
            };
            ActivitySource.AddActivityListener(_listener);
        }

        private void OnStopped(Activity activity)
        {
            var span = new SpanRecord
            {
                TraceId = activity.TraceId.ToHexString(),
                SpanId = activity.SpanId.ToHexString(),
                ParentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
                Name = activity.OperationName,
                Start = activity.StartTimeUtc.ToLocalTime(),
                DurationMs = Math.Round(activity.Duration.TotalMilliseconds, 2),
                Outcome = activity.GetTagItem("outcome")?.ToString() ?? "Unknown",
                Details = activity.GetTagItem("outcome.details")?.ToString(),
                Steps = activity.Events.Select(e => e.Name).ToList()
            };

            foreach (var tag in activity.TagObjects)
            {
                if (tag.Key is "outcome" or "outcome.details")
                    continue;
                span.Tags[tag.Key] = tag.Value?.ToString();
            }

            lock (_lock)
            {
                _spans.Add(span);
                _writer.WriteLine(JsonSerializer.Serialize(span, JsonOptions));
            }
        }

        public List<SpanRecord> Spans()
        {
            lock (_lock)
                return _spans.ToList();
        }

        public void Dispose()
        {
            _listener.Dispose();
            lock (_lock)
                _writer.Dispose();
        }
    }
}
