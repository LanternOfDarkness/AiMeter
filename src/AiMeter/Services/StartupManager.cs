using System;
using Microsoft.Win32;

namespace AiMeter.Services;

/// <summary>
/// Toggles "launch at Windows startup" via the per-user Run registry key. The registry is the
/// single source of truth (Windows reads it at sign-in), so the settings checkbox reflects and
/// writes it directly rather than mirroring it into settings.json.
/// </summary>
public class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AiMeter";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null) return;

        if (enabled)
        {
            // Quote the path so a space in the install location doesn't split the command.
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
