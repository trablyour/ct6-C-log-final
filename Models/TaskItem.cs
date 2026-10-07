namespace TaskManager.Models
{
    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public bool IsDone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public override string ToString() => $"#{Id} \"{Title}\"";
    }

    public enum TaskPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    // результат операции для интерфейса
    public class OperationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";

        public static OperationResult Ok(string message) => new() { Success = true, Message = message };
        public static OperationResult Fail(string message) => new() { Success = false, Message = message };
    }
}
