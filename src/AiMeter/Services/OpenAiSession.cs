using System;
using System.Security.Cryptography;
using System.Text;
using AiMeter.Managers;
using Microsoft.Extensions.Logging;

namespace AiMeter.Services;

/// <summary>
/// Holds the OpenAI Platform API key on behalf of <see cref="Providers.OpenAiProvider"/>. The key
/// is a real secret, so it is encrypted at rest with Windows DPAPI (CurrentUser scope) and only
/// ever kept in <see cref="Models.AppConfig.OpenAiApiKeyProtected"/> as a base64 blob — the
/// plaintext key never touches settings.json. Mirrors the other providers' session role
/// (<see cref="ClaudeSession"/>, <see cref="OpenCodeSession"/>) but authenticates by key, not cookie.
/// </summary>
public class OpenAiSession : IOpenAiSession
{
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<OpenAiSession>? _logger;

    public OpenAiSession(ISettingsManager settingsManager, ILogger<OpenAiSession>? logger = null)
    {
        _settingsManager = settingsManager;
        _logger = logger;
    }

    public string ProviderName => "OpenAI";

    public bool HasSession => !string.IsNullOrEmpty(_settingsManager.Current.OpenAiApiKeyProtected);

    public void Store(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Clear();
            return;
        }

        _settingsManager.Current.OpenAiApiKeyProtected = Protect(apiKey.Trim());
        _settingsManager.Save();
    }

    public string? GetApiKey()
    {
        var protectedKey = _settingsManager.Current.OpenAiApiKeyProtected;
        return string.IsNullOrEmpty(protectedKey) ? null : Unprotect(protectedKey);
    }

    public void Clear()
    {
        _settingsManager.Current.OpenAiApiKeyProtected = null;
        _settingsManager.Save();
    }

    private static string Protect(string plaintext)
    {
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private string? Unprotect(string protectedBase64)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Corrupt blob, or copied from another Windows user/machine (DPAPI is user-scoped).
            _logger?.LogWarning(ex, "Stored OpenAI API key could not be decrypted; treating as not configured");
            return null;
        }
    }
}
