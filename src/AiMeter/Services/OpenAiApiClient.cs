using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace AiMeter.Services;

/// <summary>
/// Talks to api.openai.com with a plain <see cref="HttpClient"/>. Unlike the Claude/OpenCode/ChatGPT
/// web surfaces, api.openai.com is a programmatic REST API with no Cloudflare browser challenge, so
/// no WebView2 is needed — the API key in the Authorization header is the only credential.
/// </summary>
public class OpenAiApiClient : IOpenAiApiClient, IDisposable
{
    private readonly HttpClient _http;

    public OpenAiApiClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AiMeter/1.0");
    }

    public async Task<(int StatusCode, string? Body)> GetAsync(string path, string? bearerToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        try
        {
            using var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Network down / DNS / timeout — surface as a transport failure (status 0) so the
            // provider reports "Error Fetching" rather than throwing.
            return (0, null);
        }
    }

    public void Dispose() => _http.Dispose();
}
