using System.Text.Json;

namespace TaskManager.Reports
{
    public class LogRecord
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "Information";
        public string Message { get; set; } = "";
        public string? EventName { get; set; }
        public string? SessionId { get; set; }
        public string? ErrorId { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
        public string? OperationPath { get; set; }
    }

    // читает структурированный лог logs/structured-ГГГГММДД.json
    public class LogAnalyzer
    {
        private readonly string _folder;

        public LogAnalyzer(string folder)
        {
            _folder = folder;
        }

        public List<LogRecord> ReadCurrentSession(string sessionId)
        {
            var result = new List<LogRecord>();
            if (!Directory.Exists(_folder))
                return result;

            foreach (string path in Directory.GetFiles(_folder, "structured-*.json").OrderBy(f => f))
            {
                // FileShare.ReadWrite: файл в это время открыт Serilog
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        var record = Parse(line);
                        if (record.SessionId == sessionId)
                            result.Add(record);
                    }
                    catch (JsonException)
                    {
                        // недописанная строка
                    }
                }
            }

            return result;
        }

        // CLEF: @t - время, @l - уровень (для Information не пишется), @m - сообщение
        private static LogRecord Parse(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            string? eventName = null;
            if (root.TryGetProperty("EventId", out var eventId) && eventId.ValueKind == JsonValueKind.Object &&
                eventId.TryGetProperty("Name", out var name))
                eventName = name.GetString();

            return new LogRecord
            {
                Time = root.TryGetProperty("@t", out var t) ? t.GetDateTime().ToLocalTime() : DateTime.MinValue,
                Level = root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information",
                Message = root.TryGetProperty("@m", out var m) ? m.GetString() ?? "" : "",
                EventName = eventName,
                SessionId = Get(root, "SessionId"),
                ErrorId = Get(root, "ErrorId"),
                ExceptionType = Get(root, "ExceptionType"),
                ExceptionMessage = Get(root, "ExceptionMessage"),
                OperationPath = Get(root, "OperationPath")
            };
        }

        private static string? Get(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
