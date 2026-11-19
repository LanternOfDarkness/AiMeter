using System;
using System.Globalization;
using System.IO;
using System.Reflection;
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

    /// <summary>The AiMeter version stamped on every line (e.g. "2.0.2+4ae9bbe"), from the assembly metadata.</summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(FileLogger).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Keep a short source revision so two builds of the same version stay distinguishable.
            var plus = informational.IndexOf('+');
            if (plus < 0) return informational;
            var revision = informational[(plus + 1)..];
            if (revision.Length > 7) revision = revision[..7];
            return $"{informational[..plus]}+{revision}";
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : version.ToString(3);
    }

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
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [v{Version}] [{logLevel}] [{CategoryName}] {message}");

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