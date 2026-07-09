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
        
        webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        webView.Source = new Uri("https://claude.ai/login");
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (webView.Source.ToString().Contains("claude.ai/chats"))
        {
            // Login successful
            var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
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
