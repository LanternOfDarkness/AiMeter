using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using AiMeter.Interop;
using AiMeter.Services;
using Microsoft.Web.WebView2.Core;

namespace AiMeter.Views;

/// <summary>
/// Drives the opencode.ai login in an embedded browser, mirroring <see cref="AuthWindow"/>.
/// opencode.ai/auth uses an OAuth round trip (opencode.ai → auth.opencode.ai → callback), so
/// login is detected when the browser leaves the main site for the auth provider and then
/// returns to opencode.ai with cookies set.
/// </summary>
public partial class OpenCodeAuthWindow : Window
{
    private readonly IOpenCodeSession _session;
    private bool _sawAuthProvider;

    public OpenCodeAuthWindow(IOpenCodeSession session)
    {
        InitializeComponent();
        SourceInitialized += (s, e) => DarkTitleBar.ApplyToHwnd(new WindowInteropHelper(this).Handle);
        _session = session;
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        var userDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiMeter", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        webView.Source = new Uri("https://opencode.ai/auth");
    }

    private async void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var uri = new Uri(webView.Source.ToString());
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        var onMainSite = host is "opencode.ai" or "www.opencode.ai";

        // Any other host (auth.opencode.ai or a downstream IdP) means we're mid-login.
        if (!onMainSite)
        {
            _sawAuthProvider = true;
            return;
        }

        // Back on the main site after visiting the auth provider (or already past the initial
        // /auth landing) — the OAuth round trip is complete once cookies are set.
        var pastAuthLanding = _sawAuthProvider || !path.StartsWith("/auth");
        if (!pastAuthLanding) return;

        var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://opencode.ai");
        if (cookies.Count > 0)
        {
            _session.Store(cookies.Select(c => (c.Name, c.Value)));

            MessageBox.Show("Successfully logged into OpenCode!", "AiMeter", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }
    }
}
