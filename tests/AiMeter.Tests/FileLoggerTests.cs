using System;
using System.IO;
using System.Linq;
using AiMeter.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AiMeter.Tests;

public class FileLoggerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AiMeterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void LogMessage(ILogger logger, string message) =>
        logger.Log<object>(LogLevel.Information, new EventId(0), null, null,
            (_, _) => message);

    [Fact]
    public void Log_writes_a_line_containing_the_message()
    {
        var dir = NewTempDir();
        try
        {
            var logger = new FileLogger(dir, "TestCategory");

            LogMessage(logger, "something happened");

            var logFile = Directory.GetFiles(dir, "aimeter-*.log").Single();
            File.ReadAllText(logFile).Should().Contain("something happened");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Log_appends_so_multiple_lines_are_all_present()
    {
        var dir = NewTempDir();
        try
        {
            var logger = new FileLogger(dir, "TestCategory");

            LogMessage(logger, "first");
            LogMessage(logger, "second");
            LogMessage(logger, "third");

            var content = File.ReadAllText(Directory.GetFiles(dir, "aimeter-*.log").Single());
            content.Should().Contain("first");
            content.Should().Contain("second");
            content.Should().Contain("third");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Log_includes_the_exception_when_one_is_provided()
    {
        var dir = NewTempDir();
        try
        {
            var logger = new FileLogger(dir, "TestCategory");
            var ex = new InvalidOperationException("boom");

            logger.Log<object>(LogLevel.Error, new EventId(0), null, ex,
                (_, _) => "fetch failed");

            var content = File.ReadAllText(Directory.GetFiles(dir, "aimeter-*.log").Single());
            content.Should().Contain("fetch failed");
            content.Should().Contain("InvalidOperationException");
            content.Should().Contain("boom");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}