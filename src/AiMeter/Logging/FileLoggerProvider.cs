using System;
using Microsoft.Extensions.Logging;

namespace AiMeter.Logging;

/// <summary>
/// Creates one <see cref="FileLogger"/> per logger category, all writing to the same
/// daily log file under the configured directory.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;

    public string FilePrefix { get; }
    public string DirectoryPath => _directory;

    public FileLoggerProvider(string directory, string filePrefix = "aimeter")
    {
        _directory = directory;
        FilePrefix = filePrefix;
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(_directory, categoryName) { FilePrefix = FilePrefix };

    public void Dispose() { }
}