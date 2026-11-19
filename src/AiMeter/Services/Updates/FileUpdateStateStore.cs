using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AiMeter.Services.Updates;

/// <summary>
/// JSON store at <c>%AppData%\AiMeter\update-state.json</c>. Writes go to a temp file first
/// and are then moved over the real file, so a crash cannot leave a truncated state behind.
/// Read/write failures log a warning and fall back to an empty state; they never throw.
/// </summary>
public sealed class FileUpdateStateStore : IUpdateStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly ILogger<FileUpdateStateStore>? _logger;

    public FileUpdateStateStore(ILogger<FileUpdateStateStore>? logger = null)
        : this(DefaultFilePath(), logger)
    {
    }

    public FileUpdateStateStore(string filePath, ILogger<FileUpdateStateStore>? logger = null)
    {
        _filePath = filePath;
        _logger = logger;
    }

    private static string DefaultFilePath()
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataFolder, "AiMeter", "update-state.json");
    }

    /// <inheritdoc />
    public UpdateState Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UpdateState();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UpdateState>(json, JsonOptions) ?? new UpdateState();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error loading update state from {Path}; starting with an empty state", _filePath);
            return new UpdateState();
        }
    }

    /// <inheritdoc />
    public void Save(UpdateState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(state, JsonOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error saving update state to {Path}", _filePath);
        }
    }
}
