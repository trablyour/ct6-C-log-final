using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;

namespace TaskManager.Logging
{
    // настройка Serilog
    public static class LoggingSetup
    {
        public const string LogsFolder = "logs";

        private const string ConsoleTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        private const string FileTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        public static string? SeqUrl => Environment.GetEnvironmentVariable("TASKMANAGER_SEQ_URL");
        public static string? SentryDsn => Environment.GetEnvironmentVariable("TASKMANAGER_SENTRY_DSN");

        public static Serilog.ILogger Create(bool jsonConsole)
        {
            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)

                // свойства, которые добавляются к каждой записи
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "TaskManager")
                .Enrich.WithProperty("Version", "1.0.0")
                .Enrich.WithProperty("SessionId", Session.Id)
                .Enrich.WithProperty("MachineName", Environment.MachineName);

            // 1. Консоль: обычный цветной текст или JSON (запуск с --json-console)
            if (jsonConsole)
                config.WriteTo.Console(new RenderedCompactJsonFormatter(), restrictedToMinimumLevel: LogEventLevel.Information);
            else
                config.WriteTo.Console(outputTemplate: ConsoleTemplate, theme: AnsiConsoleTheme.Code,
                                       restrictedToMinimumLevel: LogEventLevel.Information);

            // 2. Файлы с ротацией: новый файл каждый день или при превышении 10 МБ, хранятся 14 последних
            config.WriteTo.File(Path.Combine(LogsFolder, "log-.txt"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 14)
                .WriteTo.File(Path.Combine(LogsFolder, "errors-.txt"),
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                // структурированный лог: одна строка = один JSON-объект (формат CLEF)
                .WriteTo.File(new RenderedCompactJsonFormatter(),
                    Path.Combine(LogsFolder, "structured-.json"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 14);

            // 3. (опционально) Seq - веб-интерфейс для поиска по логам
            if (!string.IsNullOrEmpty(SeqUrl))
                config.WriteTo.Seq(SeqUrl);

            // 3. (опционально) Sentry - серьезные ошибки
            if (!string.IsNullOrEmpty(SentryDsn))
            {
                config.WriteTo.Sentry(o =>
                {
                    o.Dsn = SentryDsn;
                    o.MinimumEventLevel = LogEventLevel.Error;
                    o.MinimumBreadcrumbLevel = LogEventLevel.Information;
                    o.Release = "TaskManager@1.0.0";
                });
            }

            return config.CreateLogger();
        }
    }
}
