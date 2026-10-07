namespace TaskManager.UI
{
    // цветной вывод; общий lock, чтобы строки трассировки и меню не перемешивались
    public static class ConsoleOutput
    {
        public static readonly object Lock = new();

        public static void Write(string text, ConsoleColor color)
        {
            lock (Lock)
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(text);
                Console.ForegroundColor = old;
            }
        }

        public static void Info(string text) => Write(text, ConsoleColor.Gray);
        public static void Header(string text) => Write(text, ConsoleColor.Cyan);
        public static void Success(string text) => Write("✓ " + text, ConsoleColor.Green);
        public static void Error(string text) => Write("✗ " + text, ConsoleColor.Red);
        public static void Warning(string text) => Write(text, ConsoleColor.Yellow);
    }
}
