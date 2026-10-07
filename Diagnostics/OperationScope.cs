using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskManager.ErrorHandling;
using TaskManager.Logging;

namespace TaskManager.Diagnostics
{
    // создает трассируемые операции
    public class OperationTracer
    {
        private readonly ILogger<OperationTracer> _logger;

        public OperationTracer(ILogger<OperationTracer> logger)
        {
            _logger = logger;
        }

        public OperationScope Start(string operation) => new(operation, _logger);
    }

    // одна операция:
    //   TraceEvent(Start) -> Stopwatch -> шаги (Verbose) -> TraceEvent(Stop) с временем и результатом
    // плюс Activity (TraceId/SpanId) и scope логирования: все логи внутри операции получают ее имя
    public sealed class OperationScope : IDisposable
    {
        private static int _nextId;
        private static readonly AsyncLocal<int> Depth = new();

        private readonly ILogger _logger;
        private readonly string _name;
        private readonly int _id;
        private readonly int _depth;
        private readonly Stopwatch _stopwatch;
        private readonly Activity? _activity;
        private readonly IDisposable? _logScope;
        private int _steps;
        private bool _finished;

        public string Name => _name;
        public string? TraceId => _activity?.TraceId.ToHexString();

        internal OperationScope(string name, ILogger logger)
        {
            _name = name;
            _logger = logger;
            _id = Interlocked.Increment(ref _nextId);
            _depth = Depth.Value;
            Depth.Value = _depth + 1;

            _activity = AppTracing.Activities.StartActivity(name);

            _logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["Operation"] = name,
                ["OperationId"] = _id,
                ["TraceId"] = _activity?.TraceId.ToHexString(),
                ["SpanId"] = _activity?.SpanId.ToHexString()
            });

            // 1. до выполнения операции - событие Start
            AppTracing.Source.TraceEvent(TraceEventType.Start, _id, "{0}{1}: начало", Indent, name);

            // 2. запускаем секундомер
            _stopwatch = Stopwatch.StartNew();
        }

        private string Indent => new string(' ', _depth * 4);

        public OperationScope Tag(string key, object? value)
        {
            _activity?.SetTag(key, value);
            return this;
        }

        public void Step(string description)
        {
            _steps++;
            _activity?.AddEvent(new ActivityEvent(description));
            AppTracing.Source.TraceEvent(TraceEventType.Verbose, _id, "{0}    шаг {1}: {2}", Indent, _steps, description);
        }

        public void Success(string? details = null) => Finish(Outcome.Success, details);

        // операция не выполнена из-за действий пользователя (пустое название, задача не найдена)
        public void Warning(string details) => Finish(Outcome.Warning, details);

        // операция прервана ошибкой
        public void Error(Exception ex)
        {
            ErrorContext.Capture(_name, TraceId);
            _activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message }
            }));
            Finish(Outcome.Error, $"{ex.GetType().Name}: {ex.Message}");
        }

        public void Dispose()
        {
            // не вызвали Success/Warning/Error - значит операция прервана исключением
            if (!_finished)
            {
                ErrorContext.Capture(_name, TraceId);
                Finish(Outcome.Error, "прервана исключением");
            }
        }

        private void Finish(Outcome outcome, string? details)
        {
            if (_finished)
                return;
            _finished = true;

            // 3. останавливаем секундомер и пишем Stop с временем выполнения
            _stopwatch.Stop();
            double ms = Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 2);

            if (outcome == Outcome.Warning)
                AppTracing.Source.TraceEvent(TraceEventType.Warning, _id, "{0}    предупреждение: {1}", Indent, details);
            else if (outcome == Outcome.Error)
                AppTracing.Source.TraceEvent(TraceEventType.Error, _id, "{0}    ошибка: {1}", Indent, details);

            AppTracing.Source.TraceEvent(TraceEventType.Stop, _id, "{0}{1}: конец, {2}, {3:0.00} мс{4}",
                Indent, _name, OutcomeText(outcome), ms,
                string.IsNullOrEmpty(details) || outcome != Outcome.Success ? "" : $" ({details})");

            _logger.LogDebug(LogEvents.OperationFinished,
                "Операция {Operation} завершена: {Outcome} за {ElapsedMs} мс, шагов {Steps}",
                _name, outcome.ToString(), ms, _steps);

            if (_activity != null)
            {
                _activity.SetTag("outcome", outcome.ToString());
                _activity.SetTag("outcome.details", details);
                _activity.SetStatus(outcome == Outcome.Error ? ActivityStatusCode.Error : ActivityStatusCode.Ok, details);
                _activity.Stop();
            }

            _logScope?.Dispose();
            Depth.Value = _depth;
        }

        public static string OutcomeText(Outcome outcome) => outcome switch
        {
            Outcome.Success => "УСПЕХ",
            Outcome.Warning => "ПРЕДУПРЕЖДЕНИЕ",
            _ => "ОШИБКА"
        };
    }
}
