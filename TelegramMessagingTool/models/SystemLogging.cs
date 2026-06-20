using System;
using System.IO;
using System.Text;
using TelegramMessagingTool;
using TelegramMessagingTool.models;

namespace TelegramMessagingTool.Models
{
    internal sealed class SystemLogging
    {
        public static SystemLogging Instance { get; } = new SystemLogging();

        private readonly string _logFilePath;
        private readonly object _lock = new object();

        private SystemLogging()
        {
            string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            var fileName = $"{DateTime.Now:yyyy-MM-dd}.log";
            _logFilePath = Path.Combine(logsDirectory, fileName);

            if (!File.Exists(_logFilePath))
            {
                File.Create(_logFilePath).Close();
            }
        }

        public void Log(
     long chatId,
     string userName,
     string message,
     string response,
     LogType logType)
        {
            try
            {
                string safeMessage = TelegramMessageFormatter.RedactForLogs(message);
                string safeResponse = TelegramMessageFormatter.RedactForLogs(response);

                string logEntry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                    $"| Type: {logType} " +
                    $"| ChatID: {chatId} " +
                    $"| UserName: {TelegramMessageFormatter.RedactForLogs(userName, 100)} " +
                    $"| Message: {safeMessage} " +
                    $"| Response: {safeResponse} |";

                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Do not stop the bot if logging fails
            }
        }
    }
}