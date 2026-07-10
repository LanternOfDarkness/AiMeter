using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace AiMeter.Logging;

/// <summary>
/// Minimal logger that appends timestamped lines to a daily log file under
/// %AppData%\AiMeter\logs\aimeter-{yyyy-MM-dd}.log. All FileLogger instances share a
/// lock so writes from concurrent categories never interleave corruptly.
/// </summary>
public sealed class FileLogger : ILogger
{
    private static readonly object FileLock = new();

    public string DirectoryPath { get; }
    public string CategoryName { get; }
    public string FilePrefix { get; init; } = "aimeter";

    public FileLogger(string directoryPath, string categoryName)
    {
        DirectoryPath = directoryPath;
        CategoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter?.Invoke(state, exception) ?? state?.ToString() ?? string.Empty;

        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] [{CategoryName}] {message}");

        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }
        line += Environment.NewLine;

        var path = PathForToday();

        lock (FileLock)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(path, line);
        }
    }

    private string PathForToday() => Path.Combine(DirectoryPath,
        $"{FilePrefix}-{DateTime.Now:yyyy-MM-dd}.log");

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}