using System;
using System.IO;
using AiMeter.Logging;
using Microsoft.Extensions.Logging;

namespace AiMeter;

public static class LoggingExtensions
{
    /// <summary>
    /// Adds the file logger writing to %AppData%\AiMeter\logs\aimeter-{yyyy-MM-dd}.log.
    /// </summary>
    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder)
    {
        builder.AddProvider(new FileLoggerProvider(LogPaths.Directory));
        return builder;
    }
}