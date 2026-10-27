using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BrowseLens.Infrastructure.Diagnostics
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class LocalDiagnosticLogger
    {
        private static readonly object _fileLock = new object();
        private readonly string _logFilePath;
        private readonly long _maxFileSizeBytes;

        private static readonly Regex UrlSanitizationRegex = new Regex(
            @"(https?://[^\s\?#]+)(\?[^\s#]*)?(#[^\s]*)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string LogFilePath => _logFilePath;

        public LocalDiagnosticLogger(string? customLogPath = null, long maxFileSizeBytes = 5 * 1024 * 1024)
        {
            if (!string.IsNullOrEmpty(customLogPath))
            {
                _logFilePath = customLogPath;
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logDir = Path.Combine(localAppData, "BrowseLens", "logs");
                _logFilePath = Path.Combine(logDir, "browselens_diagnostics.log");
            }

            _maxFileSizeBytes = maxFileSizeBytes;
            EnsureDirectory();
        }

        private void EnsureDirectory()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }
        }

        public void Log(LogLevel level, string category, string message, Exception? exception = null)
        {
            string sanitizedMessage = SanitizeMessage(message);
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
            string entry = $"[{timestamp}] [{level.ToString().ToUpperInvariant(),-7}] [{category}] {sanitizedMessage}";

            if (exception != null)
            {
                entry += Environment.NewLine + $"    Exception: {exception.GetType().Name} - {exception.Message}";
            }

            WriteToFile(entry);
        }

        public void LogInfo(string category, string message) => Log(LogLevel.Info, category, message);
        public void LogWarning(string category, string message) => Log(LogLevel.Warning, category, message);
        public void LogError(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);

        public static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            // Strip query string and fragment from any URLs in log text to prevent PII/token leakage (FR-012)
            string sanitized = UrlSanitizationRegex.Replace(message, "$1");

            // Mask any authentication tokens or secrets if present
            if (sanitized.Contains("Token", StringComparison.OrdinalIgnoreCase))
            {
                sanitized = Regex.Replace(sanitized, @"(Token\s*[:=]\s*)([A-Za-z0-9_\-]{8,})", "$1[REDACTED]", RegexOptions.IgnoreCase);
            }

            return sanitized;
        }

        private void WriteToFile(string entry)
        {
            lock (_fileLock)
            {
                try
                {
                    EnsureDirectory();
                    RotateIfNecessary();

                    using var writer = File.AppendText(_logFilePath);
                    writer.WriteLine(entry);
                }
                catch
                {
                    // Logging failure should never crash the host application
                }
            }
        }

        private void RotateIfNecessary()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length >= _maxFileSizeBytes)
                    {
                        string backupPath = _logFilePath + ".bak";
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }
                        File.Move(_logFilePath, backupPath);
                    }
                }
            }
            catch { }
        }
    }
}

