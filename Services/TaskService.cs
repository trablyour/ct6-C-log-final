using Microsoft.Extensions.Logging;
using TaskManager.Diagnostics;
using TaskManager.ErrorHandling;
using TaskManager.Logging;
using TaskManager.Models;
using TaskManager.Storage;

namespace TaskManager.Services
{
    // операции с задачами. Каждая операция:
    //   - трассируется (Start / шаги / Stop с временем и результатом),
    //   - пишет структурированные логи,
    //   - сама ловит исключения и логирует их как Error
    public class TaskService
    {
        public const int MaxTitleLength = 100;
        public const int MaxDescriptionLength = 500;

        private readonly JsonTaskRepository _repository;
        private readonly ILogger<TaskService> _logger;
        private readonly OperationTracer _tracer;
        private readonly ErrorHandler _errors;
        private readonly List<TaskItem> _tasks;

        public TaskService(JsonTaskRepository repository, ILogger<TaskService> logger,
                           OperationTracer tracer, ErrorHandler errors)
        {
            _repository = repository;
            _logger = logger;
            _tracer = tracer;
            _errors = errors;
            _tasks = _repository.Load();
        }

        // ======================= 1. Добавление =======================

        public OperationResult AddTask(string? title, string? description, TaskPriority priority)
        {
            using var op = _tracer.Start("AddTask").Tag("task.priority", priority.ToString());

            try
            {
                op.Step("проверка названия и описания");
                title = title?.Trim() ?? "";
                description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

                if (title.Length == 0)
                {
                    _logger.LogWarning(LogEvents.ValidationFailed, "Ошибка ввода: пустое название задачи");
                    op.Warning("пустое название");
                    return OperationResult.Fail("Название задачи не может быть пустым");
                }

                if (title.Length > MaxTitleLength)
                {
                    _logger.LogWarning(LogEvents.ValidationFailed,
                        "Ошибка ввода: название длиной {Length} символов, допустимо {MaxLength}", title.Length, MaxTitleLength);
                    op.Warning("слишком длинное название");
                    return OperationResult.Fail($"Название не должно быть длиннее {MaxTitleLength} символов");
                }

                if (description?.Length > MaxDescriptionLength)
                {
                    _logger.LogWarning(LogEvents.ValidationFailed,
                        "Ошибка ввода: описание длиной {Length} символов, допустимо {MaxLength}", description.Length, MaxDescriptionLength);
                    op.Warning("слишком длинное описание");
                    return OperationResult.Fail($"Описание не должно быть длиннее {MaxDescriptionLength} символов");
                }

                op.Step("создание задачи");
                var task = new TaskItem
                {
                    Id = NextId(),
                    Title = title,
                    Description = description,
                    Priority = priority,
                    CreatedAt = DateTime.Now
                };
                _tasks.Add(task);

                op.Step("сохранение в файл");
                try
                {
                    _repository.Save(_tasks);
                }
                catch
                {
                    _tasks.Remove(task);   // откат: в памяти не должно остаться несохраненной задачи
                    throw;
                }

                op.Tag("task.id", task.Id);

                // {TaskTitle} - отдельное поле, @Task - вся задача как объект
                using (_logger.BeginScope(new Dictionary<string, object?> { ["@Task"] = task }))
                {
                    _logger.LogInformation(LogEvents.TaskAdded,
                        "Добавлена задача {TaskTitle} (Id {TaskId}, приоритет {Priority})",
                        task.Title, task.Id, task.Priority);
                }

                op.Success($"задача #{task.Id}");
                return OperationResult.Ok($"Задача #{task.Id} \"{task.Title}\" добавлена");
            }
            catch (Exception ex) when (ex is not CriticalException)
            {
                op.Error(ex);
                var info = _errors.Report(ex, "AddTask", context: new Dictionary<string, object?> { ["TaskTitle"] = title });
                return OperationResult.Fail($"{info.UserMessage} Код ошибки: {info.ErrorId}");
            }
        }

        // ======================= 2. Удаление (по Id или названию) =======================

        public OperationResult RemoveTask(string? query)
        {
            using var op = _tracer.Start("RemoveTask").Tag("query", query);

            try
            {
                op.Step("поиск задачи");
                query = query?.Trim() ?? "";

                if (query.Length == 0)
                {
                    _logger.LogWarning(LogEvents.ValidationFailed, "Ошибка ввода: не указана задача для удаления");
                    op.Warning("пустой запрос");
                    return OperationResult.Fail("Укажите Id или название задачи");
                }

                List<TaskItem> found = int.TryParse(query, out int id)
                    ? _tasks.Where(t => t.Id == id).ToList()
                    : _tasks.Where(t => t.Title.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();

                if (found.Count == 0)
                {
                    _logger.LogError(LogEvents.TaskNotFound, "Ошибка: задача не найдена (запрос {Query})", query);
                    op.Warning("задача не найдена");
                    return OperationResult.Fail($"Задача \"{query}\" не найдена");
                }

                if (found.Count > 1)
                {
                    _logger.LogWarning(LogEvents.AmbiguousTitle,
                        "Найдено {Count} задач с названием {TaskTitle}: {TaskIds}", found.Count, query, found.Select(t => t.Id));
                    op.Warning("несколько задач с таким названием");
                    return OperationResult.Fail($"Найдено несколько задач \"{query}\" (Id: {string.Join(", ", found.Select(t => t.Id))}). Укажите Id");
                }

                var task = found[0];
                op.Tag("task.id", task.Id);

                op.Step($"удаление задачи #{task.Id}");
                int index = _tasks.IndexOf(task);
                _tasks.RemoveAt(index);

                op.Step("сохранение в файл");
                try
                {
                    _repository.Save(_tasks);
                }
                catch
                {
                    _tasks.Insert(index, task);   // откат
                    throw;
                }

                using (_logger.BeginScope(new Dictionary<string, object?> { ["@Task"] = task }))
                {
                    _logger.LogInformation(LogEvents.TaskRemoved, "Задача \"{TaskTitle}\" удалена (Id {TaskId})", task.Title, task.Id);
                }

                op.Success($"удалена #{task.Id}");
                return OperationResult.Ok($"Задача #{task.Id} \"{task.Title}\" удалена");
            }
            catch (Exception ex) when (ex is not CriticalException)
            {
                op.Error(ex);
                var info = _errors.Report(ex, "RemoveTask", context: new Dictionary<string, object?> { ["Query"] = query });
                return OperationResult.Fail($"{info.UserMessage} Код ошибки: {info.ErrorId}");
            }
        }

        // ======================= 3. Вывод списка =======================

        public IReadOnlyList<TaskItem> ListTasks()
        {
            using var op = _tracer.Start("ListTasks");

            try
            {
                op.Step("сортировка: сначала невыполненные, затем по приоритету");
                var result = _tasks
                    .OrderBy(t => t.IsDone)
                    .ThenByDescending(t => t.Priority)
                    .ThenBy(t => t.Id)
                    .ToList();

                op.Tag("tasks.count", result.Count);

                _logger.LogInformation(LogEvents.TasksListed,
                    "Показан список из {Count} задач (выполнено {Done}, в работе {Active})",
                    result.Count, result.Count(t => t.IsDone), result.Count(t => !t.IsDone));

                op.Success($"{result.Count} задач");
                return result;
            }
            catch (Exception ex)
            {
                op.Error(ex);
                _errors.Report(ex, "ListTasks");
                return Array.Empty<TaskItem>();
            }
        }

        // ======================= дополнительно =======================

        public OperationResult CompleteTask(int id)
        {
            using var op = _tracer.Start("CompleteTask").Tag("task.id", id);

            try
            {
                op.Step($"поиск задачи #{id}");
                var task = _tasks.FirstOrDefault(t => t.Id == id);

                if (task == null)
                {
                    _logger.LogError(LogEvents.TaskNotFound, "Ошибка: задача не найдена (Id {TaskId})", id);
                    op.Warning("задача не найдена");
                    return OperationResult.Fail($"Задача #{id} не найдена");
                }

                if (task.IsDone)
                {
                    _logger.LogWarning(LogEvents.ValidationFailed, "Задача #{TaskId} уже выполнена {CompletedAt}", id, task.CompletedAt);
                    op.Warning("уже выполнена");
                    return OperationResult.Fail($"Задача #{id} уже выполнена");
                }

                op.Step("отметка выполнения");
                task.IsDone = true;
                task.CompletedAt = DateTime.Now;

                op.Step("сохранение в файл");
                try
                {
                    _repository.Save(_tasks);
                }
                catch
                {
                    task.IsDone = false;
                    task.CompletedAt = null;
                    throw;
                }

                double minutes = Math.Round((task.CompletedAt.Value - task.CreatedAt).TotalMinutes, 1);
                _logger.LogInformation(LogEvents.TaskCompleted,
                    "Задача \"{TaskTitle}\" (Id {TaskId}) выполнена за {MinutesToComplete} мин", task.Title, task.Id, minutes);

                op.Success($"выполнена #{id}");
                return OperationResult.Ok($"Задача #{id} отмечена выполненной");
            }
            catch (Exception ex) when (ex is not CriticalException)
            {
                op.Error(ex);
                var info = _errors.Report(ex, "CompleteTask", context: new Dictionary<string, object?> { ["TaskId"] = id });
                return OperationResult.Fail($"{info.UserMessage} Код ошибки: {info.ErrorId}");
            }
        }

        public OperationResult ImportTasks(string path)
        {
            using var op = _tracer.Start("ImportTasks").Tag("file.path", path);

            try
            {
                op.Step("чтение файла");
                var imported = _repository.Import(path);

                if (imported == null)
                {
                    _logger.LogError(LogEvents.StorageError, "Ошибка: файл для импорта не найден {FilePath}", path);
                    op.Warning("файл не найден");
                    return OperationResult.Fail($"Файл {path} не найден");
                }

                op.Step("добавление задач с новыми Id");
                var valid = imported.Where(t => !string.IsNullOrWhiteSpace(t.Title)).ToList();
                foreach (var task in valid)
                {
                    task.Id = NextId();
                    _tasks.Add(task);
                }

                op.Step("сохранение в файл");
                _repository.Save(_tasks);

                _logger.LogInformation(LogEvents.TasksImported,
                    "Импорт из {FilePath}: добавлено {Added}, пропущено {Skipped}", path, valid.Count, imported.Count - valid.Count);

                op.Success($"импортировано {valid.Count}");
                return OperationResult.Ok($"Импортировано задач: {valid.Count}");
            }
            catch (Exception ex) when (ex is not CriticalException)
            {
                op.Error(ex);
                var info = _errors.Report(ex, "ImportTasks", context: new Dictionary<string, object?> { ["FilePath"] = path });
                return OperationResult.Fail($"{info.UserMessage} Код ошибки: {info.ErrorId}");
            }
        }

        // ======================= симуляция ошибок =======================

        // добавление, когда файл данных занят другим процессом -> IOException -> StorageException
        public OperationResult SimulateLockedFile()
        {
            using var op = _tracer.Start("SimulateLockedFile");

            op.Step("файл данных открывается другим процессом");
            OperationResult result;
            using (_repository.LockDataFile())
            {
                op.Step("попытка добавить задачу");
                result = AddTask("Задача при занятом файле", null, TaskPriority.Low);
            }

            op.Success(result.Success ? "блокировка не сработала" : "ошибка обработана");
            return result;
        }

        // ошибка в коде без локального try/catch: ее поймает централизованный обработчик
        public OperationResult SimulateBug()
        {
            using var op = _tracer.Start("CalculateStatistics");

            op.Step("поиск последней выполненной задачи");
            TaskItem? lastDone = _tasks
                .Where(t => t.IsDone && t.Title == "такой задачи нет")
                .FirstOrDefault();

            op.Step("расчет времени выполнения");
            // ОШИБКА В КОДЕ (специально): lastDone может быть null
            double minutes = (lastDone!.CompletedAt!.Value - lastDone.CreatedAt).TotalMinutes;

            op.Success();
            return OperationResult.Ok($"Последняя задача выполнена за {minutes:0} мин");
        }

        // критическая ошибка: программа должна завершиться (Fatal)
        public OperationResult SimulateCritical()
        {
            using var op = _tracer.Start("CheckIntegrity");

            op.Step("проверка уникальности Id");
            throw new CriticalException("Нарушена целостность данных: найдены задачи с одинаковым Id")
                .With("DuplicateId", 1)
                .With("TasksCount", _tasks.Count);
        }

        private int NextId() => _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;
    }
}
