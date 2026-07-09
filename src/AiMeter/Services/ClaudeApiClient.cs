using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AiMeter.Services;

/// <summary>
/// Fetches claude.ai JSON endpoints through a hidden, persistent WebView2 instance
/// instead of a raw HttpClient. claude.ai sits behind Cloudflare bot management,
/// which blocks non-browser clients even when the session cookie is valid - a real
/// browser engine is the only thing that reliably gets through.
///
/// Results come back via window.chrome.webview.postMessage rather than
/// ExecuteScriptAsync's return value, since ExecuteScriptAsync does not reliably
/// await a returned Promise (it can resolve to the Promise's own JSON shape, "{}",
/// before the fetch completes).
/// </summary>
public class ClaudeApiClient : IClaudeApiClient, IDisposable
{
    private Window? _hostWindow;
    private WebView2? _webView;
    private Task? _initTask;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public async Task<(int StatusCode, string? Body)> GetAsync(string path)
    {
        await EnsureInitializedAsync();

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

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var navigationComplete = new TaskCompletionSource();
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            navigationComplete.TrySetResult();
        }
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.Navigate("https://claude.ai");
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
