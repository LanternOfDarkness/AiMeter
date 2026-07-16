using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using AiMeter.Interop;
using AiMeter.Services;
using Microsoft.Web.WebView2.Core;

namespace AiMeter.Views;

public partial class AuthWindow : Window
{
    private readonly IClaudeSession _session;

    public AuthWindow(IClaudeSession session)
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
        webView.Source = new Uri("https://claude.ai/login");
    }

    private async void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var url = webView.Source.ToString().ToLower();
        if (url.Contains("claude.ai") && !url.Contains("/login"))
        {
            // Potential login successful
            var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");

            if (cookies.Any(c => c.Name == "sessionKey"))
            {
                _session.Store(cookies.Select(c => (c.Name, c.Value)));

                MessageBox.Show("Successfully logged into Claude!", "AiMeter", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
        }
    }
}
