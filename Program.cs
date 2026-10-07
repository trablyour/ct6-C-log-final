using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TaskManager.Alerts;
using TaskManager.Diagnostics;
using TaskManager.ErrorHandling;
using TaskManager.Logging;
using TaskManager.Reports;
using TaskManager.Services;
using TaskManager.Storage;
using TaskManager.UI;

namespace TaskManager
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            bool demo = args.Contains("--demo");
            bool fatal = args.Contains("--fatal");
            bool jsonConsole = args.Contains("--json-console");

            // ---------- логирование (Serilog) ----------
            Log.Logger = LoggingSetup.Create(jsonConsole);

            // ---------- трассировка (TraceSource + Activity) ----------
            using var traceSetup = new TraceSetup(LoggingSetup.LogsFolder,
                demo ? ConsoleTraceMode.StartStop : ConsoleTraceMode.Off);
            using var collector = new TraceCollector(LoggingSetup.LogsFolder);

            var session = Stopwatch.StartNew();
            AppTracing.Source.TraceEvent(TraceEventType.Start, 0, "Сеанс {0}: начало", Session.Id);

            // контейнер создается до try, чтобы логгер и обработчик ошибок были доступны в catch и finally
            using var services = BuildServices(traceSetup, collector);
            var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("TaskManager.Program");
            var errors = services.GetRequiredService<ErrorHandler>();
            int exitCode = 0;

            try
            {
                errors.RegisterGlobalHandlers();

                var alerts = services.GetRequiredService<AlertService>();
                log.LogInformation(LogEvents.AppStarted,
                    "TaskManager запущен: сеанс {SessionId}, ОС {OS}, .NET {Runtime}",
                    Session.Id, RuntimeInformation.OSDescription, Environment.Version.ToString());
                log.LogInformation("Оповещения: {Channels}; Seq: {Seq}; Sentry: {Sentry}",
                    alerts.Channels, LoggingSetup.SeqUrl ?? "выкл", string.IsNullOrEmpty(LoggingSetup.SentryDsn) ? "выкл" : "вкл");

                // Ctrl+C: аккуратно записываем завершение
                var logger = log;
                Console.CancelKeyPress += (_, _) =>
                {
                    logger.LogWarning(LogEvents.AppCancelled, "Программа прервана пользователем (Ctrl+C)");
                    AppTracing.Source.TraceEvent(TraceEventType.Stop, 0, "Сеанс {0}: прерван", Session.Id);
                    Log.CloseAndFlush();
                };

                var ui = services.GetRequiredService<ConsoleUI>();
                if (demo)
                    ui.RunDemo(fatal);
                else
                    ui.Run();
            }
            catch (Exception ex)
            {
                // глобальный перехват: все, что не обработано ниже (включая CriticalException)
                Log.Fatal(ex, "Необработанная ошибка");

                // подробная запись (стек, операция, контекст) и оповещение
                errors.Report(ex, "Main", LogLevel.Critical);

                ConsoleOutput.Error("Произошла критическая ошибка. Подробнее см. логи.");
                exitCode = 1;
            }
            finally
            {
                double seconds = Math.Round(session.Elapsed.TotalSeconds, 1);

                log.LogInformation(LogEvents.AppStopping,
                    "Программа завершается (код {ExitCode}, сеанс {SessionSeconds} с)", exitCode, seconds);

                // конец сеанса в трассировке
                AppTracing.Source.TraceEvent(TraceEventType.Stop, 0, "Сеанс {0}: конец, {1} с, код {2}", Session.Id, seconds, exitCode);

                Log.CloseAndFlush();
            }

            return exitCode;
        }

        // внедрение зависимостей: ILogger<T> для всех классов, Serilog - провайдер логирования
        private static ServiceProvider BuildServices(TraceSetup traceSetup, TraceCollector collector)
        {
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddSerilog(dispose: false);
            });

            services.AddSingleton(traceSetup);
            services.AddSingleton(collector);
            services.AddSingleton<OperationTracer>();

            services.AddSingleton(sp =>
            {
                var alerts = new AlertService(sp.GetRequiredService<ILogger<AlertService>>())
                    .Add(new ConsoleAlertChannel(), LogLevel.Error)
                    .Add(new FileAlertChannel(LoggingSetup.LogsFolder), LogLevel.Error);

                var email = EmailAlertChannel.FromEnvironment();
                if (email != null)
                    alerts.Add(email, LogLevel.Critical);

                return alerts;
            });

            services.AddSingleton<ErrorHandler>();

            services.AddSingleton(sp => new JsonTaskRepository(
                Path.Combine("data", "tasks.json"),
                sp.GetRequiredService<ILogger<JsonTaskRepository>>(),
                sp.GetRequiredService<OperationTracer>(),
                sp.GetRequiredService<ErrorHandler>()));

            services.AddSingleton<TaskService>();
            services.AddSingleton(new LogAnalyzer(LoggingSetup.LogsFolder));
            services.AddSingleton<ConsoleUI>();

            return services.BuildServiceProvider();
        }
    }
}
