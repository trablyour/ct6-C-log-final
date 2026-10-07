using Microsoft.Extensions.Logging;
using TaskManager.Diagnostics;
using TaskManager.ErrorHandling;
using TaskManager.Logging;
using TaskManager.Models;
using TaskManager.Reports;
using TaskManager.Services;
using static TaskManager.UI.ConsoleOutput;

namespace TaskManager.UI
{
    // консольное меню. Каждое действие выполняется через ErrorHandler.Execute
    public class ConsoleUI
    {
        private readonly TaskService _service;
        private readonly ErrorHandler _errors;
        private readonly TraceSetup _traceSetup;
        private readonly TraceCollector _collector;
        private readonly LogAnalyzer _analyzer;
        private readonly ILogger<ConsoleUI> _logger;

        public ConsoleUI(TaskService service, ErrorHandler errors, TraceSetup traceSetup,
                         TraceCollector collector, LogAnalyzer analyzer, ILogger<ConsoleUI> logger)
        {
            _service = service;
            _errors = errors;
            _traceSetup = traceSetup;
            _collector = collector;
            _analyzer = analyzer;
            _logger = logger;
        }

        // ======================= меню =======================

        public void Run()
        {
            while (true)
            {
                PrintMenu();
                Console.Write("Выберите пункт: ");
                string? choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                if (choice == null)   // конец ввода (Ctrl+Z / закрытый поток)
                    return;

                _logger.LogDebug(LogEvents.MenuSelected, "Выбран пункт меню {MenuItem}", choice);

                switch (choice)
                {
                    case "1": Run("AddTask", AddTaskDialog); break;
                    case "2": Run("RemoveTask", RemoveTaskDialog); break;
                    case "3": View("ListTasks", () => PrintTasks(_service.ListTasks())); break;
                    case "4": Run("CompleteTask", CompleteTaskDialog); break;
                    case "5": Run("ImportTasks", ImportDialog); break;
                    case "6": ReportsMenu(); break;
                    case "7": SimulationMenu(); break;
                    case "8": TraceModeMenu(); break;
                    case "0":
                        _logger.LogInformation(LogEvents.AppStopping, "Пользователь выбрал выход");
                        return;
                    default:
                        _logger.LogWarning(LogEvents.InvalidInput, "Неизвестный пункт меню {MenuItem}", choice);
                        Error("Нет такого пункта меню");
                        break;
                }

                Console.WriteLine();
            }
        }

        private void PrintMenu()
        {
            Header("================== TaskManager ==================");
            Console.WriteLine("  1. Добавить задачу");
            Console.WriteLine("  2. Удалить задачу (по Id или названию)");
            Console.WriteLine("  3. Показать список задач");
            Console.WriteLine("  4. Отметить задачу выполненной");
            Console.WriteLine("  5. Импорт задач из JSON-файла");
            Console.WriteLine("  6. Отчеты (трассировка, ошибки, логи)");
            Console.WriteLine("  7. Симуляция ошибок");
            Console.WriteLine($"  8. Трассировка в консоли: {ModeText(_traceSetup.ConsoleMode)}");
            Console.WriteLine("  0. Выход");
        }

        private void Run(string operation, Func<OperationResult> action) =>
            ShowResult(_errors.Execute(operation, action));

        private void View(string operation, Action action)
        {
            var result = _errors.Execute(operation, () =>
            {
                action();
                return OperationResult.Ok("");
            });

            if (!result.Success)
                ShowResult(result);
        }

        // ======================= диалоги =======================

        private OperationResult AddTaskDialog()
        {
            Console.Write("Название: ");
            string? title = Console.ReadLine();

            Console.Write("Описание (необязательно): ");
            string? description = Console.ReadLine();

            Console.Write("Приоритет (1 - низкий, 2 - средний, 3 - высокий) [2]: ");
            string? input = Console.ReadLine()?.Trim();

            var priority = TaskPriority.Medium;
            if (!string.IsNullOrEmpty(input))
            {
                if (int.TryParse(input, out int p) && Enum.IsDefined(typeof(TaskPriority), p))
                    priority = (TaskPriority)p;
                else
                    _logger.LogWarning(LogEvents.InvalidInput, "Некорректный приоритет {Input}, использован {Priority}", input, priority);
            }

            return _service.AddTask(title, description, priority);
        }

        private OperationResult RemoveTaskDialog()
        {
            Console.Write("Id или название задачи: ");
            return _service.RemoveTask(Console.ReadLine());
        }

        private OperationResult CompleteTaskDialog()
        {
            Console.Write("Id задачи: ");
            string? input = Console.ReadLine()?.Trim();

            if (int.TryParse(input, out int id) && id > 0)
                return _service.CompleteTask(id);

            _logger.LogWarning(LogEvents.InvalidInput, "Некорректный Id {Input}", input);
            return OperationResult.Fail("Id должен быть положительным числом");
        }

        private OperationResult ImportDialog()
        {
            Console.Write("Путь к JSON-файлу: ");
            return _service.ImportTasks(Console.ReadLine()?.Trim().Trim('"') ?? "");
        }

        // ======================= подменю =======================

        private void ReportsMenu()
        {
            Console.WriteLine(" 1. Отчет по трассировке операций");
            Console.WriteLine(" 2. Журнал ошибок текущего сеанса");
            Console.WriteLine(" 3. Статистика по логам текущего сеанса");
            Console.WriteLine(" 4. Файлы логов");
            Console.Write("Выберите: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": View("TraceReport", ShowTraceReport); break;
                case "2": View("ErrorJournal", ShowErrorJournal); break;
                case "3": View("LogStats", ShowLogStats); break;
                case "4": View("LogFiles", ShowLogFiles); break;
                default: Error("Нет такого пункта"); break;
            }
        }

        private void SimulationMenu()
        {
            Console.WriteLine(" 1. Файл данных занят другим процессом (IOException, ловится в AddTask)");
            Console.WriteLine(" 2. Импорт поврежденного файла (JsonException, ловится в ImportTasks)");
            Console.WriteLine(" 3. Ошибка в коде (NullReferenceException, ловится централизованно)");
            Console.WriteLine(" 4. Критический сбой (Fatal, программа завершится)");
            Console.Write("Выберите: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": Run("SimulateLockedFile", _service.SimulateLockedFile); break;
                case "2": Run("ImportTasks", () => _service.ImportTasks(CreateBrokenFile())); break;
                case "3": Run("CalculateStatistics", _service.SimulateBug); break;
                case "4": Run("CheckIntegrity", _service.SimulateCritical); break;
                default: Error("Нет такого пункта"); break;
            }
        }

        private void TraceModeMenu()
        {
            Console.WriteLine(" 1. Выключена");
            Console.WriteLine(" 2. Только Start / Stop, предупреждения и ошибки");
            Console.WriteLine(" 3. Подробно (с шагами)");
            Console.Write("Выберите: ");

            var mode = Console.ReadLine()?.Trim() switch
            {
                "1" => ConsoleTraceMode.Off,
                "3" => ConsoleTraceMode.Verbose,
                _ => ConsoleTraceMode.StartStop
            };

            _traceSetup.SetConsoleMode(mode);
            _logger.LogInformation("Режим трассировки в консоли: {TraceMode}", mode);
        }

        private static string ModeText(ConsoleTraceMode mode) => mode switch
        {
            ConsoleTraceMode.Off => "выключена",
            ConsoleTraceMode.Verbose => "подробно",
            _ => "Start / Stop"
        };

        private static string CreateBrokenFile()
        {
            Directory.CreateDirectory("data");
            string path = Path.Combine("data", "broken.json");
            File.WriteAllText(path, "[ { \"Title\": \"ok\" }, { это не JSON ]");
            return path;
        }

        // ======================= отчеты =======================

        private void ShowTraceReport()
        {
            var spans = _collector.Spans();
            if (spans.Count == 0)
            {
                Info("Операций еще не было");
                return;
            }

            Header("Отчет по трассировке операций (текущий сеанс)");
            Info($" {"Операция",-20} {"вызовов",8} {"успех",7} {"предупр.",9} {"ошибка",7} {"среднее,мс",11} {"макс,мс",9}");
            Info(" " + new string('-', 76));

            foreach (var g in spans.GroupBy(s => s.Name).OrderBy(g => g.Key))
            {
                int ok = g.Count(s => s.Outcome == "Success");
                int warn = g.Count(s => s.Outcome == "Warning");
                int err = g.Count(s => s.Outcome == "Error");
                var color = err > 0 ? ConsoleColor.Red : warn > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray;

                Write($" {g.Key,-20} {g.Count(),8} {ok,7} {warn,9} {err,7} {g.Average(s => s.DurationMs),11:0.00} {g.Max(s => s.DurationMs),9:0.00}", color);
            }

            Console.WriteLine();
            Header(" Последние трассы:");
            foreach (var root in spans.Where(s => s.ParentSpanId == null).TakeLast(6))
                PrintSpan(root, spans, 1);

            Info($" Все трассы: {_collector.FilePath}");
        }

        private static void PrintSpan(SpanRecord span, List<SpanRecord> all, int level)
        {
            string indent = new string(' ', level * 2) + (level > 1 ? "└ " : "");
            string outcome = Enum.TryParse<Outcome>(span.Outcome, out var o) ? OperationScope.OutcomeText(o) : span.Outcome;
            string details = string.IsNullOrEmpty(span.Details) ? "" : $" - {span.Details}";
            var color = span.Outcome switch
            {
                "Error" => ConsoleColor.Red,
                "Warning" => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };

            Write($"{indent}{span.Start:HH:mm:ss} {span.Name}: {outcome}, {span.DurationMs:0.00} мс{details}", color);

            foreach (var child in all.Where(s => s.ParentSpanId == span.SpanId).OrderBy(s => s.Start))
                PrintSpan(child, all, level + 1);
        }

        private void ShowErrorJournal()
        {
            var errors = _analyzer.ReadCurrentSession(Session.Id)
                .Where(r => r.EventName == LogEvents.ExceptionCaught.Name)
                .ToList();

            Header($"Журнал ошибок (сеанс {Session.Id})");
            if (errors.Count == 0)
            {
                Info(" Ошибок нет");
                return;
            }

            foreach (var r in errors)
            {
                var color = r.Level == "Fatal" ? ConsoleColor.Magenta : ConsoleColor.Red;
                Write($" {r.Time:HH:mm:ss}  {r.Level,-6} код {r.ErrorId}  {r.ExceptionType}", color);
                Info($"           операция:  {r.OperationPath}");
                Info($"           сообщение: {r.ExceptionMessage}");
            }

            Info(" Стек вызовов: logs/errors-ГГГГММДД.txt и поле StackTrace в logs/structured-ГГГГММДД.json");
        }

        private void ShowLogStats()
        {
            var records = _analyzer.ReadCurrentSession(Session.Id);
            Header($"Статистика логов (сеанс {Session.Id}): {records.Count} записей");

            Info(" По уровням:");
            foreach (var g in records.GroupBy(r => r.Level).OrderByDescending(g => g.Count()))
                Info($"   {g.Key,-12} {g.Count(),5}");

            Info(" По событиям:");
            foreach (var g in records.Where(r => r.EventName != null && r.EventName != LogEvents.OperationFinished.Name)
                                     .GroupBy(r => r.EventName!)
                                     .OrderByDescending(g => g.Count()))
                Info($"   {g.Key,-18} {g.Count(),5}");
        }

        private static void ShowLogFiles()
        {
            if (!Directory.Exists(LoggingSetup.LogsFolder))
            {
                Info("Папка logs пуста");
                return;
            }

            Info($"Папка: {Path.GetFullPath(LoggingSetup.LogsFolder)}");
            foreach (var file in new DirectoryInfo(LoggingSetup.LogsFolder).GetFiles().OrderBy(f => f.Name))
                Info($"  {file.Name,-28} {file.Length,9} байт   {file.LastWriteTime:dd.MM.yyyy HH:mm:ss}");
        }

        // ======================= демонстрация =======================

        public void RunDemo(bool withFatal)
        {
            _logger.LogInformation("Запущен демонстрационный сценарий");

            Step(1, "Добавление задач");
            Run("AddTask", () => _service.AddTask("Подготовить итоговый отчет", "Собрать скриншоты и описание", TaskPriority.High));
            Run("AddTask", () => _service.AddTask("Купить продукты", "Молоко, хлеб", TaskPriority.Low));
            Run("AddTask", () => _service.AddTask("Сдать контрольную точку", null, TaskPriority.Medium));

            Step(2, "Пустое название (ошибка ввода)");
            Run("AddTask", () => _service.AddTask("   ", null, TaskPriority.Medium));

            Step(3, "Список задач");
            View("ListTasks", () => PrintTasks(_service.ListTasks()));

            Step(4, "Удаление по названию");
            Run("RemoveTask", () => _service.RemoveTask("Купить продукты"));

            Step(5, "Удаление несуществующей задачи");
            Run("RemoveTask", () => _service.RemoveTask("Несуществующая задача"));

            Step(6, "Импорт поврежденного файла (исключение ловится в методе)");
            Run("ImportTasks", () => _service.ImportTasks(CreateBrokenFile()));

            Step(7, "Добавление при занятом файле (исключение ловится в AddTask, изменения откатываются)");
            Run("SimulateLockedFile", _service.SimulateLockedFile);

            Step(8, "Ошибка в коде (исключение ловится централизованно)");
            Run("CalculateStatistics", _service.SimulateBug);

            Step(9, "Программа продолжает работать");
            View("ListTasks", () => PrintTasks(_service.ListTasks()));

            Step(10, "Отчет по трассировке");
            ShowTraceReport();

            Step(11, "Журнал ошибок");
            ShowErrorJournal();

            if (withFatal)
            {
                Step(12, "Критический сбой: программа завершится");
                Run("CheckIntegrity", _service.SimulateCritical);
            }
            else
            {
                Console.WriteLine();
                Info("Чтобы увидеть Fatal: dotnet run -- --demo --fatal");
            }

            Console.WriteLine();
            ShowLogFiles();
        }

        private static void Step(int number, string title)
        {
            Console.WriteLine();
            Header($"--- Шаг {number}. {title} ---");
        }

        // ======================= вывод =======================

        private static void PrintTasks(IReadOnlyList<TaskItem> list)
        {
            if (list.Count == 0)
            {
                Info("Список задач пуст");
                return;
            }

            Console.WriteLine();
            Info($" {"Id",-4} {"Статус",-8} {"Приоритет",-10} {"Создана",-17} {"Название",-30} Описание");
            Info(" " + new string('-', 100));

            foreach (var t in list)
            {
                var color = t.IsDone ? ConsoleColor.DarkGray
                    : t.Priority == TaskPriority.High ? ConsoleColor.Yellow
                    : ConsoleColor.White;

                string status = t.IsDone ? "готово" : "в работе";
                string priority = t.Priority switch
                {
                    TaskPriority.High => "высокий",
                    TaskPriority.Low => "низкий",
                    _ => "средний"
                };

                Write($" {t.Id,-4} {status,-8} {priority,-10} {t.CreatedAt,-17:dd.MM.yyyy HH:mm} {Cut(t.Title, 30),-30} {t.Description ?? "-"}", color);
            }

            Info($" Всего: {list.Count}, выполнено: {list.Count(t => t.IsDone)}");
        }

        private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

        private static void ShowResult(OperationResult result)
        {
            if (result.Success)
                Success(result.Message);
            else
                Error(result.Message);
        }
    }
}
