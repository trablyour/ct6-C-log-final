using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using TaskManager.Logging;
using TaskManager.UI;

namespace TaskManager.Alerts
{
    public class Alert
    {
        public string ErrorId { get; init; } = "";
        public LogLevel Level { get; init; }
        public string Operation { get; init; } = "";
        public string ExceptionType { get; init; } = "";
        public string Message { get; init; } = "";
        public string? InnerMessage { get; init; }
        public DateTime Time { get; init; }
    }

    public interface IAlertChannel
    {
        string Name { get; }
        void Send(Alert alert);
    }

    // рассылка оповещений; одинаковые ошибки - не чаще раза в 30 секунд (кроме критических)
    public class AlertService
    {
        private readonly ILogger<AlertService> _logger;
        private readonly List<(IAlertChannel Channel, LogLevel MinLevel)> _channels = new();
        private readonly Dictionary<string, DateTime> _lastSent = new();
        private readonly TimeSpan _throttle = TimeSpan.FromSeconds(30);

        public AlertService(ILogger<AlertService> logger)
        {
            _logger = logger;
        }

        public AlertService Add(IAlertChannel channel, LogLevel minLevel)
        {
            _channels.Add((channel, minLevel));
            return this;
        }

        public IEnumerable<string> Channels => _channels.Select(c => $"{c.Channel.Name} (от {c.MinLevel})");

        public void Notify(Alert alert)
        {
            string key = $"{alert.ExceptionType}|{alert.Operation}";

            if (alert.Level < LogLevel.Critical &&
                _lastSent.TryGetValue(key, out var last) && alert.Time - last < _throttle)
            {
                _logger.LogInformation(LogEvents.AlertSuppressed,
                    "Оповещение {ErrorId} подавлено: такая же ошибка была {SecondsAgo} с назад",
                    alert.ErrorId, Math.Round((alert.Time - last).TotalSeconds));
                return;
            }

            _lastSent[key] = alert.Time;

            foreach (var (channel, minLevel) in _channels.Where(c => alert.Level >= c.MinLevel))
            {
                try
                {
                    channel.Send(alert);
                    _logger.LogDebug(LogEvents.AlertSent, "Оповещение {ErrorId} отправлено: {Channel}", alert.ErrorId, channel.Name);
                }
                catch (Exception ex)
                {
                    // сбой оповещения не должен ронять программу
                    _logger.LogWarning(LogEvents.AlertFailed, ex, "Не удалось отправить оповещение {ErrorId} в {Channel}",
                        alert.ErrorId, channel.Name);
                }
            }
        }
    }

    // "!!! Ошибка !!!" в консоли
    public class ConsoleAlertChannel : IAlertChannel
    {
        public string Name => "Console";

        public void Send(Alert alert)
        {
            bool critical = alert.Level == LogLevel.Critical;
            var color = critical ? ConsoleColor.Magenta : ConsoleColor.Red;
            string line = new string('!', 64);

            lock (ConsoleOutput.Lock)
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine();
                Console.WriteLine(line);
                Console.WriteLine($"!!!  {(critical ? "КРИТИЧЕСКАЯ ОШИБКА" : "ОШИБКА")}  (код {alert.ErrorId})");
                Console.WriteLine($"!!!  Операция: {alert.Operation}");
                Console.WriteLine($"!!!  {alert.ExceptionType}: {alert.Message}");
                if (alert.InnerMessage != null)
                    Console.WriteLine($"!!!  Причина:  {alert.InnerMessage}");
                Console.WriteLine(line);
                Console.WriteLine();
                Console.ForegroundColor = old;
            }
        }
    }

    // отдельный журнал оповещений: logs/alerts-ГГГГММДД.txt
    public class FileAlertChannel : IAlertChannel
    {
        private readonly string _folder;

        public FileAlertChannel(string folder)
        {
            _folder = folder;
        }

        public string Name => "File";

        public void Send(Alert alert)
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, $"alerts-{DateTime.Now:yyyyMMdd}.txt");
            File.AppendAllText(path,
                $"[{alert.Time:yyyy-MM-dd HH:mm:ss}] {alert.Level} код {alert.ErrorId}{Environment.NewLine}" +
                $"    Операция: {alert.Operation}{Environment.NewLine}" +
                $"    {alert.ExceptionType}: {alert.Message}{Environment.NewLine}" +
                (alert.InnerMessage != null ? $"    Причина: {alert.InnerMessage}{Environment.NewLine}" : ""));
        }
    }

    // письмо при критической ошибке; настраивается переменными окружения (см. README)
    public class EmailAlertChannel : IAlertChannel
    {
        private readonly string _host, _user, _password, _to;
        private readonly int _port;

        private EmailAlertChannel(string host, int port, string user, string password, string to)
        {
            (_host, _port, _user, _password, _to) = (host, port, user, password, to);
        }

        public string Name => $"Email ({_to})";

        public static EmailAlertChannel? FromEnvironment()
        {
            string? host = Environment.GetEnvironmentVariable("TASKMANAGER_SMTP_HOST");
            string? user = Environment.GetEnvironmentVariable("TASKMANAGER_SMTP_USER");
            string? password = Environment.GetEnvironmentVariable("TASKMANAGER_SMTP_PASSWORD");
            string? to = Environment.GetEnvironmentVariable("TASKMANAGER_ALERT_EMAIL");

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) ||
                string.IsNullOrEmpty(password) || string.IsNullOrEmpty(to))
                return null;

            int port = int.TryParse(Environment.GetEnvironmentVariable("TASKMANAGER_SMTP_PORT"), out int p) ? p : 587;
            return new EmailAlertChannel(host, port, user, password, to);
        }

        public void Send(Alert alert)
        {
            using var client = new SmtpClient(_host, _port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_user, _password),
                Timeout = 10_000
            };

            using var message = new MailMessage(_user, _to)
            {
                Subject = $"[TaskManager] {alert.Level}: {alert.ExceptionType} (код {alert.ErrorId})",
                Body = $"Время: {alert.Time:yyyy-MM-dd HH:mm:ss}\nКомпьютер: {Environment.MachineName}\n" +
                       $"Операция: {alert.Operation}\nОшибка: {alert.ExceptionType}: {alert.Message}\n" +
                       (alert.InnerMessage != null ? $"Причина: {alert.InnerMessage}\n" : "") +
                       "\nПодробности в логах по коду ошибки."
            };

            client.Send(message);
        }
    }
}
