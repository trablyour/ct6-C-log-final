namespace TaskManager.ErrorHandling
{
    // базовое исключение приложения; контекст ошибки хранится в Exception.Data
    public class TaskManagerException : Exception
    {
        public TaskManagerException(string message, Exception? inner = null) : base(message, inner)
        {
        }

        public TaskManagerException With(string key, object? value)
        {
            Data[key] = value;
            return this;
        }
    }

    // ошибка чтения или записи хранилища (оборачивает IOException, JsonException)
    public class StorageException : TaskManagerException
    {
        public StorageException(string message, string filePath, Exception? inner = null) : base(message, inner)
        {
            Data["FilePath"] = filePath;
        }
    }

    // критическая ошибка: продолжать работу нельзя (уровень Fatal)
    public class CriticalException : TaskManagerException
    {
        public CriticalException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }
}
