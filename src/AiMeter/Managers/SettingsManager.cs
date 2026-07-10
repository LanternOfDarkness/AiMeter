using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AiMeter.Models;
using Microsoft.Extensions.Logging;

namespace AiMeter.Managers;

public class SettingsManager : ISettingsManager
{
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsManager>? _logger;

    public AppConfig Current { get; private set; }

    public SettingsManager(ILogger<SettingsManager>? logger = null)
    {
        _logger = logger;
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataFolder, "AiMeter");
        Directory.CreateDirectory(appFolder);

        _settingsFilePath = Path.Combine(appFolder, "settings.json");

        Current = new AppConfig();
        Load();
    }

    public void Load()
    {
        if (File.Exists(_settingsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loadedConfig = JsonSerializer.Deserialize<AppConfig>(json);
                if (loadedConfig != null)
                {
                    // Populate the existing singleton in place (bound to the UI) so that
                    // adding a new setting never requires touching this method again.
                    foreach (var property in typeof(AppConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!property.CanRead || !property.CanWrite) continue;
                        property.SetValue(Current, property.GetValue(loadedConfig));
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback to defaults on error
                _logger?.LogError(ex, "Error loading settings from {Path}", _settingsFilePath);
            }
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving settings to {Path}", _settingsFilePath);
        }
    }
}
