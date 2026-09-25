using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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

    public async Task<CaptureResult> CaptureResponsesAsync(string pageUrl, IReadOnlyList<string> apiPaths)
    {
        await EnsureInitializedAsync();
        var core = _webView!.CoreWebView2;
        var responses = new List<CapturedResponse>();
        var primaryAnswered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bouncedToLogin = false;

        async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (e.Request.Method != "GET" || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
            var path = apiPaths.FirstOrDefault(p => uri.AbsolutePath.EndsWith(p, StringComparison.OrdinalIgnoreCase));
            if (path is null) return;

            var status = e.Response.StatusCode;
            var orgId = e.Request.Headers.Contains("x-org-id") ? e.Request.Headers.GetHeader("x-org-id") : null;
            string? body = null;
            try
            {
                using var stream = await e.Response.GetContentAsync();
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    body = await reader.ReadToEndAsync();
                }
            }
            catch (Exception)
            {
                // Body unavailable (e.g. aborted request); keep the status.
            }

            responses.Add(new CapturedResponse(path, status, body, orgId));
            if (path == apiPaths[0])
            {
                primaryAnswered.TrySetResult();
            }
        }

        // The console is a SPA: an unauthenticated visit is redirected client-side to
        // /console/login and the API calls never happen, so end early instead of timing out.
        void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (CurrentUrl.Contains("/console/login", StringComparison.OrdinalIgnoreCase))
            {
                bouncedToLogin = true;
                primaryAnswered.TrySetResult();
            }
        }

        core.WebResourceResponseReceived += OnResponseReceived;
        core.SourceChanged += OnSourceChanged;
        try
        {
            core.Navigate(pageUrl);
            await Task.WhenAny(primaryAnswered.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            // Let the page's other queries (and any re-query once the org resolves) land too.
            if (primaryAnswered.Task.IsCompleted && !bouncedToLogin)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            return new CaptureResult(responses.ToList(), bouncedToLogin, CurrentUrl);
        }
        finally
        {
            core.WebResourceResponseReceived -= OnResponseReceived;
            core.SourceChanged -= OnSourceChanged;
        }
    }

    public async Task<(string FinalUrl, string? Html)> NavigateAndReadAsync(string url)
    {
        await EnsureInitializedAsync();
        var core = _webView!.CoreWebView2;
        var settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Redirect chains (opencode.ai → auth.opencode.ai → back) fire several completions;
        // only one that lands back on opencode.ai counts as settled.
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (Uri.TryCreate(CurrentUrl, UriKind.Absolute, out var uri) && uri.Host is "opencode.ai" or "www.opencode.ai")
            {
                settled.TrySetResult(e.IsSuccess);
            }
        }

        core.NavigationCompleted += OnNavigationCompleted;
        try
        {
            core.Navigate(url);
            var finished = await Task.WhenAny(settled.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            if (finished != settled.Task || !settled.Task.Result)
            {
                return (CurrentUrl, null);
            }
            var json = await core.ExecuteScriptAsync("document.documentElement.outerHTML");
            return (CurrentUrl, JsonSerializer.Deserialize<string?>(json));
        }
        finally
        {
            core.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    public async Task<string?> GetLocalStorageAsync(string key)
    {
        await EnsureInitializedAsync();
        var json = await _webView!.CoreWebView2.ExecuteScriptAsync(
            $"window.localStorage.getItem({JsonSerializer.Serialize(key)})");
        return JsonSerializer.Deserialize<string?>(json);
    }

    public async Task SetLocalStorageAsync(string key, string? value)
    {
        await EnsureInitializedAsync();
        var script = value is null
            ? $"window.localStorage.removeItem({JsonSerializer.Serialize(key)})"
            : $"window.localStorage.setItem({JsonSerializer.Serialize(key)}, {JsonSerializer.Serialize(value)})";
        await _webView!.CoreWebView2.ExecuteScriptAsync(script);
    }

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
