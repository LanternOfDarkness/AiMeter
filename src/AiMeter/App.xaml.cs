using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using AiMeter.ViewModels;
using AiMeter.Managers;
using AiMeter.Providers;
using AiMeter.Services;

namespace AiMeter;

public partial class App : Application
{
    private IHost? _host;
    private TaskbarIcon? _notifyIcon;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register Managers
                services.AddSingleton<IProviderManager, ProviderManager>();
                services.AddSingleton<ISettingsManager, SettingsManager>();
                services.AddSingleton<IClaudeSession, ClaudeSession>();
                services.AddSingleton<IClaudeApiClient, ClaudeApiClient>();

                // Register Providers
                services.AddTransient<IProvider, ClaudeWebProvider>();
#if DEBUG
                // A second adapter keeps the IProvider seam real and gives the manager
                // deterministic data to test/demo against, without shipping fake data.
                services.AddTransient<IProvider, MockProvider>();
#endif

                // Register ViewModels
                services.AddSingleton<TrayViewModel>();
                services.AddSingleton<WidgetViewModel>();
                services.AddTransient<SettingsViewModel>();

                // Register Views
                services.AddSingleton<Views.WidgetWindow>();
                services.AddTransient<Views.SettingsWindow>();
                services.AddTransient<Views.AuthWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host!.StartAsync();

        // Start monitoring usage
        var providerManager = _host.Services.GetRequiredService<IProviderManager>();
        await providerManager.StartAsync();

        // Show widget by default
        _host.Services.GetRequiredService<Views.WidgetWindow>().Show();

        base.OnStartup(e);

        _notifyIcon = (TaskbarIcon)FindResource("TrayIcon");
        if (_notifyIcon != null)
        {
            var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/app.png"));
            if (streamInfo != null)
            {
                using var bmp = new System.Drawing.Bitmap(streamInfo.Stream);
                var hIcon = bmp.GetHicon();
                _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
            }
            _notifyIcon.DataContext = _host.Services.GetRequiredService<TrayViewModel>();
            _notifyIcon.ForceCreate();
        }

        providerManager.AlertRaised += (s, alert) =>
        {
            _notifyIcon?.ShowNotification(alert.Title, alert.Message, NotificationIcon.Info);
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();

        (_host!.Services.GetService<IClaudeApiClient>() as IDisposable)?.Dispose();

        await _host!.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
