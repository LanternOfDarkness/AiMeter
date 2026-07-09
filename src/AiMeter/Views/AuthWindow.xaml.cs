using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using AiMeter.Managers;
using Microsoft.Web.WebView2.Core;

namespace AiMeter.Views;

public partial class AuthWindow : Window
{
    private readonly ISettingsManager _settingsManager;

    public AuthWindow(ISettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
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
        webView.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
        webView.Source = new Uri("https://claude.ai/login");
    }

    private void CoreWebView2_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var uri = e.Request.Uri.ToLower();
        if (uri.Contains("api") && (uri.Contains("limit") || uri.Contains("usage") || uri.Contains("organizations") || uri.Contains("stats") || uri.Contains("chat")))
        {
            try
            {
                System.IO.File.AppendAllText("claude_api_logs.txt", $"{DateTime.Now}: {e.Request.Method} {e.Request.Uri}\n");
            }
            catch { }
        }
    }

    private async void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var url = webView.Source.ToString().ToLower();
        if (url.Contains("claude.ai") && !url.Contains("/login"))
        {
            // Potential Login successful
            var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
            
            try
            {
                System.IO.File.AppendAllText("claude_api_logs.txt", $"\n--- NAVIGATED TO {url} ---\n");
                foreach (var c in cookies)
                {
                    System.IO.File.AppendAllText("claude_api_logs.txt", $"COOKIE: {c.Name}\n");
                }
            }
            catch { }

            if (cookies.Any(c => c.Name == "sessionKey"))
            {
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                
                // Encrypt cookies
                var plainBytes = Encoding.UTF8.GetBytes(cookieString);
                var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                _settingsManager.Current.EncryptedCookies = Convert.ToBase64String(encryptedBytes);
                _settingsManager.Save();

                MessageBox.Show("Successfully logged into Claude!", "AiMeter", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
        }
    }
}
