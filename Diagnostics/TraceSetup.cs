using System.Diagnostics;

namespace TaskManager.Diagnostics
{
    // подключение слушателей к TraceSource
    public sealed class TraceSetup : IDisposable
    {
        private readonly FileTraceListener _file;
        private readonly ColorConsoleTraceListener _console = new();

        public ConsoleTraceMode ConsoleMode { get; private set; }
        public string FilePath => _file.FilePath;

        public TraceSetup(string folder, ConsoleTraceMode consoleMode)
        {
            var source = AppTracing.Source;
            source.Listeners.Clear();   // убираем DefaultTraceListener

            // в файл пишется все, включая шаги
            _file = new FileTraceListener(folder);
            source.Listeners.Add(_file);

            SetConsoleMode(consoleMode);
        }

        // фильтр EventTypeFilter определяет, какие события увидит консоль
        public void SetConsoleMode(ConsoleTraceMode mode)
        {
            ConsoleMode = mode;
            var source = AppTracing.Source;
            source.Listeners.Remove(_console);

            if (mode == ConsoleTraceMode.Off)
                return;

            _console.Filter = new EventTypeFilter(mode == ConsoleTraceMode.Verbose
                ? SourceLevels.All
                : SourceLevels.Warning | SourceLevels.ActivityTracing);   // предупреждения, ошибки, Start, Stop

            source.Listeners.Add(_console);
        }

        public void Dispose()
        {
            AppTracing.Source.Flush();
            AppTracing.Source.Close();
            _file.Dispose();
        }
    }
}
