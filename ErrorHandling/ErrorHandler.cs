using Microsoft.Extensions.Logging;
using TaskManager.Alerts;
using TaskManager.Logging;
using TaskManager.Models;

namespace TaskManager.ErrorHandling
{
    public record ErrorInfo(string ErrorId, LogLevel Level, string UserMessage);

    // централизованная обработка и логирование исключений
    public class ErrorHandler
    {
        public const string UserMessage = "Произошла ошибка. Подробнее см. логи.";

        private readonly ILogger<ErrorHandler> _logger;
        private readonly AlertService _alerts;

        public ErrorHandler(ILogger<ErrorHandler> logger, AlertService alerts)
        {
            _logger = logger;
            _alerts = alerts;
        }

        // глобальные перехватчики: исключения в других потоках и в задачах (Task)
        public void RegisterGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Report(ex, "UnhandledException", LogLevel.Critical);
                Serilog.Log.CloseAndFlush();
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Report(e.Exception, "UnobservedTask", LogLevel.Error);
                e.SetObserved();
            };
        }

        // выполнить действие из меню: любое непойманное исключение логируется,
        // пользователь видит короткое сообщение, программа продолжает работу
        public OperationResult Execute(string operation, Func<OperationResult> action)
        {
            ErrorContext.Clear();

            try
            {
                return action();
            }
            catch (CriticalException)
            {
                throw;   // уходит в Main -> Fatal -> завершение
            }
            catch (Exception ex)
            {
                var info = Report(ex, operation);
                return OperationResult.Fail($"{info.UserMessage} Код ошибки: {info.ErrorId}");
            }
        }

        // записать исключение в лог: текст, стек, тип, операция, контекст, уровень
        public ErrorInfo Report(Exception ex, string operation, LogLevel? level = null,
                                IDictionary<string, object?>? context = null)
        {
            var actualLevel = level ?? (ex is CriticalException or OutOfMemoryException ? LogLevel.Critical : LogLevel.Error);
            string errorId = Guid.NewGuid().ToString("N")[..6].ToUpper();
            var (path, traceId) = ErrorContext.Take();
            string operationPath = path ?? operation;

            // контекст: то, что передал вызывающий код, плюс Exception.Data
            var allContext = new Dictionary<string, string?>();
            if (context != null)
                foreach (var pair in context)
                    allContext[pair.Key] = pair.Value?.ToString();
            foreach (System.Collections.DictionaryEntry entry in ex.Data)
                allContext[entry.Key.ToString() ?? "?"] = entry.Value?.ToString();

            var inner = new List<string>();
            for (var e = ex.InnerException; e != null; e = e.InnerException)
                inner.Add($"{e.GetType().Name}: {e.Message}");

            // ключ с @ - Serilog сохранит значение как объект, а не строку
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["ErrorId"] = errorId,
                ["OperationPath"] = operationPath,
                ["ErrorTraceId"] = traceId,
                ["ExceptionType"] = ex.GetType().FullName,
                ["ExceptionMessage"] = ex.Message,
                ["StackTrace"] = ex.StackTrace,
                ["@InnerExceptions"] = inner,
                ["@ErrorContext"] = allContext
            }))
            {
                _logger.Log(actualLevel, LogEvents.ExceptionCaught, ex,
                    "Исключение {ExceptionName} в операции {OperationPath}: {ExceptionMessage} (код {ErrorId})",
                    ex.GetType().Name, operationPath, ex.Message, errorId);
            }

            if (actualLevel >= LogLevel.Error)
            {
                _alerts.Notify(new Alert
                {
                    ErrorId = errorId,
                    Level = actualLevel,
                    Operation = operationPath,
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    InnerMessage = inner.FirstOrDefault(),
                    Time = DateTime.Now
                });
            }

            return new ErrorInfo(errorId, actualLevel,
                ex is CriticalException ? "Критическая ошибка, программа будет завершена." : UserMessage);
        }
    }
}
