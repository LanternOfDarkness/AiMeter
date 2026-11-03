using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AiMeter.Services;

/// <summary>
/// Fetches opencode.ai JSON endpoints through a hidden, persistent WebView2 instance,
/// mirroring <see cref="ClaudeApiClient"/> 1:1. Using the browser engine (rather than a raw
/// HttpClient) reuses the logged-in OAuth session cookie and survives any bot protection,
/// exactly as the Claude client does.
///
/// Results come back via window.chrome.webview.postMessage rather than ExecuteScriptAsync's
/// return value, since ExecuteScriptAsync does not reliably await a returned Promise.
/// </summary>
public class OpenCodeApiClient : IOpenCodeApiClient, IDisposable
{
    private Window? _hostWindow;
    private WebView2? _webView;
    private Task? _initTask;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public string CurrentUrl => _webView?.Source?.ToString() ?? string.Empty;

    public async Task<(int StatusCode, string? Body)> GetAsync(string path)
    {
        await EnsureInitializedAsync();

        var currentSource = _webView?.Source?.ToString().ToLowerInvariant() ?? string.Empty;
        if (currentSource.Contains("/auth") || currentSource.Contains("/login"))
        {
            await RefreshNavigationAsync();
        }

        var (status, body) = await ExecuteFetchAsync(path);
        if (status is 401 or 403 or 0)
        {
            await RefreshNavigationAsync();
            (status, body) = await ExecuteFetchAsync(path);
        }

        return (status, body);
    }

    public async Task<RenderedPage> ReadRenderedPageAsync(string url, string readyCheck)
    {
        await EnsureInitializedAsync();
        var core = _webView!.CoreWebView2;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Redirect chains (opencode.ai -> auth.opencode.ai -> back) fire several completions;
        // only the document load matters - the ready check covers the SPA's own rendering.
        // Waiting for it also keeps the check off the previous page's (identical) stale DOM.
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();

        core.NavigationCompleted += OnNavigationCompleted;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            core.Navigate(url);
            await Task.WhenAny(loaded.Task, Task.Delay(deadline - DateTime.UtcNow));

            var ready = false;
            while (!IsLoginUrl(CurrentUrl))
            {
                try
                {
                    ready = await core.ExecuteScriptAsync($"!!({readyCheck})") == "true";
                }
                catch (Exception)
                {
                    // Mid-navigation (e.g. a client-side redirect); try again.
                }
                if (ready || DateTime.UtcNow >= deadline) break;
                await Task.Delay(500);
            }

            if (IsLoginUrl(CurrentUrl))
            {
                return new RenderedPage(CurrentUrl, null, false, true);
            }
            var json = await core.ExecuteScriptAsync("document.documentElement.outerHTML");
            return new RenderedPage(CurrentUrl, JsonSerializer.Deserialize<string?>(json), ready, false);
        }
        finally
        {
            core.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private static bool IsLoginUrl(string url) =>
        url.Contains("/console/login", StringComparison.OrdinalIgnoreCase)
        || url.Contains("auth.opencode.ai", StringComparison.OrdinalIgnoreCase);

    private async Task RefreshNavigationAsync()
    {
        if (_webView is null) return;
        var navigationComplete = new TaskCompletionSource();
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            navigationComplete.TrySetResult();
        }
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.Navigate("https://opencode.ai");
        await Task.WhenAny(navigationComplete.Task, Task.Delay(10000));
    }

    private async Task<(int StatusCode, string? Body)> ExecuteFetchAsync(string path)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>();
        _pending[requestId] = tcs;

        var script = $$"""
            (function() {
                fetch({{JsonSerializer.Serialize(path)}}, { credentials: 'include' })
                    .then(r => r.text().then(body => ({ status: r.status, body })))
                    .catch(e => ({ status: 0, body: String(e) }))
                    .then(result => window.chrome.webview.postMessage(JSON.stringify({
                        requestId: {{JsonSerializer.Serialize(requestId)}},
                        result
                    })));
            })();
            """;

        await _webView!.CoreWebView2.ExecuteScriptAsync(script);

        using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var registration = timeoutCts.Token.Register(() =>
        {
            _pending.TryRemove(requestId, out _);
            tcs.TrySetCanceled();
        });
        var resultJson = await tcs.Task;

        using var doc = JsonDocument.Parse(resultJson);
        var status = doc.RootElement.GetProperty("status").GetInt32();
        var body = doc.RootElement.GetProperty("body").GetString();
        return (status, body);
    }

    private Task EnsureInitializedAsync() => _initTask ??= InitializeAsync();

    private static readonly System.Threading.SemaphoreSlim WebViewInitLock = new(1, 1);

    private async Task InitializeAsync()
    {
        _hostWindow = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -32000,
            Top = -32000,
        };
        _webView = new WebView2();
        _hostWindow.Content = _webView;
        _hostWindow.Show();

        var userDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiMeter", "WebView2");

        await WebViewInitLock.WaitAsync();
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await _webView.EnsureCoreWebView2Async(env);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x800700AA) && attempt < 3)
                {
                    await Task.Delay(300);
                }
            }
        }
        finally
        {
            WebViewInitLock.Release();
        }

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var navigationComplete = new TaskCompletionSource();
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            navigationComplete.TrySetResult();
        }
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.Navigate("https://opencode.ai");
        await navigationComplete.Task;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (message is null) return;

        using var doc = JsonDocument.Parse(message);
        var requestId = doc.RootElement.GetProperty("requestId").GetString();
        if (requestId is null || !_pending.TryRemove(requestId, out var tcs)) return;

        var resultJson = doc.RootElement.GetProperty("result").GetRawText();
        tcs.TrySetResult(resultJson);
    }

    public void Dispose()
    {
        if (_webView is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        _webView?.Dispose();
        _hostWindow?.Close();
    }
}
