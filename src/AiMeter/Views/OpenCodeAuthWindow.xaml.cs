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
/// opencode.ai currently has two independent logins, and usage needs both:
///   1) the OpenCode Console (opencode.ai/console/login) — reports Go status and the user's
///      workspace id. Done once the browser reaches a /console page other than the login
///      screen (the SPA only lets an authenticated user get there).
///   2) the original site (opencode.ai/auth → auth.opencode.ai → /auth/callback) — its
///      per-workspace page still holds usage for Go plans the console doesn't show. Done once
///      the browser returns to opencode.ai from the auth server.
/// The session is stored after step 1, so closing the window during step 2 still leaves the
/// console usable.
/// </summary>
public partial class OpenCodeAuthWindow : Window
{
    private enum Step { Console, Legacy, Done }

    private readonly IOpenCodeSession _session;
    private Step _step = Step.Console;
    private bool _sawLegacyAuthServer;

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
        webView.Source = new Uri("https://opencode.ai/console/login?next=%2Fconsole%2Fgo");
    }

    private async void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var uri = new Uri(webView.Source.ToString());
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();
        var onMainSite = host is "opencode.ai" or "www.opencode.ai";

        switch (_step)
        {
            case Step.Console:
                // Other hosts (a downstream IdP) mean we're mid-login.
                if (!onMainSite || !path.StartsWith("/console") || path.StartsWith("/console/login")) return;

                var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://opencode.ai");
                if (cookies.Count == 0 || _step != Step.Console) return;

                _session.Store(cookies.Select(c => (c.Name, c.Value)));
                _step = Step.Legacy;
                Title = "Log into OpenCode (step 2 of 2: opencode.ai workspace)";
                webView.CoreWebView2.Navigate("https://opencode.ai/auth");
                break;

            case Step.Legacy:
                if (!onMainSite)
                {
                    _sawLegacyAuthServer = true;
                    return;
                }
                // Back on the main site from the auth server, past the /auth hand-off.
                if (!_sawLegacyAuthServer || path.StartsWith("/auth")) return;

                _step = Step.Done;
                MessageBox.Show("Successfully logged into OpenCode!", "AiMeter", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                break;
        }
    }
}
