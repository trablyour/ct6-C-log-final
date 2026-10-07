using System.Diagnostics;

namespace TaskManager.Diagnostics
{
    public static class AppTracing
    {
        public const string Name = "TaskManager";

        // TraceSource: события Start / Stop / Verbose / Warning / Error
        public static readonly TraceSource Source = new(Name, SourceLevels.All);

        // ActivitySource: идентификаторы трасс (TraceId, SpanId) и связь вложенных операций
        public static readonly ActivitySource Activities = new(Name, "1.0.0");
    }

    public enum Outcome
    {
        Success,
        Warning,
        Error
    }

    // режим вывода трассировки в консоль
    public enum ConsoleTraceMode
    {
        Off,
        StartStop,
        Verbose
    }
}
