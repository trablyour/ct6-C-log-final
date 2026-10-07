using System.Diagnostics;
using TaskManager.UI;

namespace TaskManager.Diagnostics
{
    // свой формат строки трассировки: "14:02:11.441 START  #12  AddTask: начало"
    public abstract class FormattedTraceListener : TraceListener
    {
        protected readonly object Sync = new();

        protected FormattedTraceListener(string name) : base(name)
        {
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id)
            => TraceEvent(eventCache, source, eventType, id, "", null);

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
            => TraceEvent(eventCache, source, eventType, id, message, null);

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
            int id, string? format, params object?[]? args)
        {
            if (Filter != null && !Filter.ShouldTrace(eventCache, source, eventType, id, format, args, null, null))
                return;

            string message = args is { Length: > 0 } ? string.Format(format ?? "", args) : format ?? "";
            string line = $"{DateTime.Now:HH:mm:ss.fff} {Label(eventType),-6} #{id,-4} {message}";

            lock (Sync)
            {
                WriteEvent(eventType, line);
            }
        }

        protected abstract void WriteEvent(TraceEventType type, string line);

        public static string Label(TraceEventType type) => type switch
        {
            TraceEventType.Start => "START",
            TraceEventType.Stop => "STOP",
            TraceEventType.Verbose => "STEP",
            TraceEventType.Information => "INFO",
            TraceEventType.Warning => "WARN",
            TraceEventType.Error => "ERROR",
            TraceEventType.Critical => "CRIT",
            _ => type.ToString().ToUpper()
        };
    }

    // трассировка в файл logs/trace-ГГГГММДД.txt
    public sealed class FileTraceListener : FormattedTraceListener
    {
        private readonly StreamWriter _writer;

        public string FilePath { get; }

        public FileTraceListener(string folder) : base("FileTrace")
        {
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, $"trace-{DateTime.Now:yyyyMMdd}.txt");
            _writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };
        }

        protected override void WriteEvent(TraceEventType type, string line) => _writer.WriteLine(line);

        public override void Write(string? message)
        {
            lock (Sync) _writer.Write(message);
        }

        public override void WriteLine(string? message)
        {
            lock (Sync) _writer.WriteLine(message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                lock (Sync) _writer.Dispose();
            base.Dispose(disposing);
        }
    }

    // трассировка в консоль, цвет зависит от типа события
    public sealed class ColorConsoleTraceListener : FormattedTraceListener
    {
        public ColorConsoleTraceListener() : base("ConsoleTrace")
        {
        }

        protected override void WriteEvent(TraceEventType type, string line)
        {
            var color = type switch
            {
                TraceEventType.Start or TraceEventType.Stop => ConsoleColor.DarkCyan,
                TraceEventType.Warning => ConsoleColor.DarkYellow,
                TraceEventType.Error or TraceEventType.Critical => ConsoleColor.DarkRed,
                _ => ConsoleColor.DarkGray
            };

            ConsoleOutput.Write("  " + line, color);
        }

        public override void Write(string? message) => ConsoleOutput.Write(message ?? "", ConsoleColor.DarkGray);

        public override void WriteLine(string? message) => ConsoleOutput.Write(message ?? "", ConsoleColor.DarkGray);
    }
}
