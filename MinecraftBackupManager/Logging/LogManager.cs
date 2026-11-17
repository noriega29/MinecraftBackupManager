using System.Globalization;
using System.Text;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging.Models;

namespace MinecraftBackupManager.Logging
{
    public sealed class LogManager
    {
        private readonly ApplicationPaths _paths;
        private readonly object _lock = new();

        public LogManager(ApplicationPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public void Information(LogCategory category, string message) => Write(category, LogLevel.Information, message);
        public void Warning(LogCategory category, string message) => Write(category, LogLevel.Warning, message);
        public void Error(LogCategory category, string message) => Write(category, LogLevel.Error, message);
        public void Critical(LogCategory category, string message) => Write(category, LogLevel.Critical, message);

        private void Write(LogCategory category, LogLevel level, string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            string filePath = GetLogFilePath(category);

            lock (_lock)
            {
                string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                string logEntry = $"{timestamp} [{level}] [{category}] {message}{Environment.NewLine}";

                Directory.CreateDirectory(_paths.LogsDirectory);
                File.AppendAllText(filePath, logEntry, Encoding.UTF8);
            }
        }

        private string GetLogFilePath(LogCategory category)
        {
            string fileName = category switch
            {
                LogCategory.Application => "application.log",
                LogCategory.Backup => "backup.log",
                LogCategory.Restoration => "restoration.log",
                _ => throw new ArgumentOutOfRangeException(nameof(category),
                category,
                "La categoría de registro no es válida.")
            };

            return Path.Combine(_paths.LogsDirectory, fileName);
        }
    }
}
