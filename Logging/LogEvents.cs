using Microsoft.Extensions.Logging;

namespace TaskManager.Logging
{
    // коды событий. В логах они хранятся в поле EventId: { "Id": 2001, "Name": "TaskAdded" }
    public static class LogEvents
    {
        // 1xxx - приложение
        public static readonly EventId AppStarted = new(1000, nameof(AppStarted));
        public static readonly EventId AppStopping = new(1001, nameof(AppStopping));
        public static readonly EventId AppCancelled = new(1002, nameof(AppCancelled));
        public static readonly EventId MenuSelected = new(1100, nameof(MenuSelected));
        public static readonly EventId InvalidInput = new(1101, nameof(InvalidInput));

        // 2xxx - операции с задачами
        public static readonly EventId TaskAdded = new(2001, nameof(TaskAdded));
        public static readonly EventId TaskRemoved = new(2002, nameof(TaskRemoved));
        public static readonly EventId TasksListed = new(2003, nameof(TasksListed));
        public static readonly EventId TaskCompleted = new(2004, nameof(TaskCompleted));
        public static readonly EventId TasksImported = new(2005, nameof(TasksImported));

        // 3xxx - ошибки пользователя
        public static readonly EventId ValidationFailed = new(3001, nameof(ValidationFailed));
        public static readonly EventId TaskNotFound = new(3002, nameof(TaskNotFound));
        public static readonly EventId AmbiguousTitle = new(3003, nameof(AmbiguousTitle));

        // 4xxx - хранилище
        public static readonly EventId StorageLoaded = new(4001, nameof(StorageLoaded));
        public static readonly EventId StorageSaved = new(4002, nameof(StorageSaved));
        public static readonly EventId StorageError = new(4003, nameof(StorageError));

        // 5xxx - трассировка
        public static readonly EventId OperationFinished = new(5001, nameof(OperationFinished));

        // 6xxx - ошибки и оповещения
        public static readonly EventId ExceptionCaught = new(6001, nameof(ExceptionCaught));
        public static readonly EventId AlertSent = new(6002, nameof(AlertSent));
        public static readonly EventId AlertSuppressed = new(6003, nameof(AlertSuppressed));
        public static readonly EventId AlertFailed = new(6004, nameof(AlertFailed));
    }

    // идентификатор запуска программы: отличает записи разных сеансов в одном файле
    public static class Session
    {
        public static readonly string Id = Guid.NewGuid().ToString("N")[..8];
        public static readonly DateTime StartedAt = DateTime.Now;
    }
}
