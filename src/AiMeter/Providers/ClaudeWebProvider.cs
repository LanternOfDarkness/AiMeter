using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AiMeter.Managers;
using AiMeter.Models;

namespace AiMeter.Providers;

public class ClaudeWebProvider : IProvider
{
    public string Name => "Claude Web";

    private readonly ISettingsManager _settingsManager;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    public ClaudeWebProvider(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = _cookieContainer };
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36");
    }

    public async Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        var metrics = new List<UsageMetric>();
        
        if (string.IsNullOrEmpty(_settingsManager.Current.EncryptedCookies))
        {
            // Not logged in
            metrics.Add(new UsageMetric { Name = "Auth Required", TotalQuota = 100, RemainingQuota = 0 });
            return metrics;
        }

        try
        {
            // Decrypt cookies
            var encryptedBytes = Convert.FromBase64String(_settingsManager.Current.EncryptedCookies);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var cookieString = Encoding.UTF8.GetString(plainBytes);

            var uri = new Uri("https://claude.ai");
            foreach (var cookiePart in cookieString.Split(';'))
            {
                var parts = cookiePart.Split('=', 2);
                if (parts.Length == 2)
                {
                    _cookieContainer.Add(uri, new Cookie(parts[0].Trim(), parts[1].Trim()));
                }
            }

            // Example request to verify auth (Replace with real usage endpoint)
            var response = await _httpClient.GetAsync("https://claude.ai/api/organizations");
            
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Session expired
                metrics.Add(new UsageMetric { Name = "Session Expired", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            // TODO: Parse the actual JSON response for usage limits.
            // For now, we simulate a successful scrape to show it works:
            metrics.Add(new UsageMetric
            {
                Name = "Claude Messages",
                TotalQuota = 100,
                RemainingQuota = 87, // Fake parsed data
                ResetTime = DateTime.Now.AddHours(3)
            });
        }
        catch (Exception ex)
        {
            metrics.Add(new UsageMetric { Name = "Error", TotalQuota = 100, RemainingQuota = 0 });
            Console.WriteLine(ex.Message);
        }

        return metrics;
    }
}
