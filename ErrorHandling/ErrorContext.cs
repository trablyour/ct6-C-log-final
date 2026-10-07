namespace TaskManager.ErrorHandling
{
    // путь операций, в которых произошло исключение: "AddTask > SaveTasks".
    // Заполняется трассировкой (OperationScope), читается обработчиком ошибок
    public static class ErrorContext
    {
        private static readonly AsyncLocal<string?> PathValue = new();
        private static readonly AsyncLocal<string?> TraceIdValue = new();

        public static void Capture(string operation, string? traceId)
        {
            // внутренняя операция фиксируется первой, внешние добавляются слева
            if (PathValue.Value == null)
                PathValue.Value = operation;
            else if (!PathValue.Value.StartsWith(operation + " >") && PathValue.Value != operation)
                PathValue.Value = $"{operation} > {PathValue.Value}";

            TraceIdValue.Value ??= traceId;
        }

        public static (string? Path, string? TraceId) Take()
        {
            var result = (PathValue.Value, TraceIdValue.Value);
            Clear();
            return result;
        }

        public static void Clear()
        {
            PathValue.Value = null;
            TraceIdValue.Value = null;
        }
    }
}
