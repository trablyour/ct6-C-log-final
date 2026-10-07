using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using TaskManager.Diagnostics;
using TaskManager.ErrorHandling;
using TaskManager.Logging;
using TaskManager.Models;

namespace TaskManager.Storage
{
    // хранение задач в JSON-файле data/tasks.json
    public class JsonTaskRepository
    {
        private readonly string _filePath;
        private readonly ILogger<JsonTaskRepository> _logger;
        private readonly OperationTracer _tracer;
        private readonly ErrorHandler _errors;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public JsonTaskRepository(string filePath, ILogger<JsonTaskRepository> logger,
                                  OperationTracer tracer, ErrorHandler errors)
        {
            _filePath = filePath;
            _logger = logger;
            _tracer = tracer;
            _errors = errors;
        }

        public string FilePath => _filePath;

        public List<TaskItem> Load()
        {
            using var op = _tracer.Start("LoadTasks");

            op.Step($"проверка файла {_filePath}");
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation(LogEvents.StorageLoaded, "Файл задач {FilePath} не найден, список пуст", _filePath);
                op.Success("файла нет");
                return new List<TaskItem>();
            }

            try
            {
                op.Step("чтение и разбор JSON");
                var tasks = ReadFile(_filePath);

                _logger.LogInformation(LogEvents.StorageLoaded, "Загружено задач: {Count} из {FilePath}", tasks.Count, _filePath);
                op.Success($"загружено {tasks.Count}");
                return tasks;
            }
            catch (StorageException ex)
            {
                // при запуске не падаем: переносим поврежденный файл и начинаем с пустого списка
                op.Step("перенос поврежденного файла в резервную копию");
                string backup = $"{_filePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(_filePath, backup);
                ex.With("BackupPath", backup);

                op.Error(ex);
                _errors.Report(ex, "LoadTasks");
                return new List<TaskItem>();
            }
        }

        public void Save(IReadOnlyCollection<TaskItem> tasks)
        {
            using var op = _tracer.Start("SaveTasks");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                op.Step("сериализация в JSON");
                string json = JsonSerializer.Serialize(tasks, JsonOptions);

                op.Step("запись в файл");
                string? folder = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(_filePath, json);

                _logger.LogDebug(LogEvents.StorageSaved, "Сохранено задач: {Count} за {ElapsedMs} мс",
                    tasks.Count, stopwatch.ElapsedMilliseconds);
                op.Success($"сохранено {tasks.Count}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                op.Error(ex);
                // системное исключение оборачиваем в свое и добавляем контекст
                throw new StorageException("Не удалось сохранить задачи", _filePath, ex)
                    .With("TasksCount", tasks.Count);
            }
        }

        // null - файла нет (ошибка пользователя); поврежденный файл - StorageException
        public List<TaskItem>? Import(string path)
        {
            using var op = _tracer.Start("ReadImportFile").Tag("file.path", path);

            op.Step($"проверка файла {path}");
            if (!File.Exists(path))
            {
                op.Warning("файл не найден");
                return null;
            }

            op.Step("чтение и разбор JSON");
            var tasks = ReadFile(path);
            op.Success($"прочитано {tasks.Count}");
            return tasks;
        }

        // для симуляции ошибки: держит файл открытым, чтобы запись в него не удалась
        public IDisposable LockDataFile()
        {
            string? folder = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            if (!File.Exists(_filePath))
                File.WriteAllText(_filePath, "[]");

            return new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        private static List<TaskItem> ReadFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new List<TaskItem>();
            }
            catch (JsonException ex)
            {
                throw new StorageException("Файл поврежден, не удалось разобрать JSON", path, ex)
                    .With("LineNumber", ex.LineNumber)
                    .With("BytePosition", ex.BytePositionInLine);
            }
            catch (IOException ex)
            {
                throw new StorageException("Ошибка чтения файла", path, ex);
            }
        }
    }
}
