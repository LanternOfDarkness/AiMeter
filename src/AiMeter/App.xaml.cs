using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            .ConfigureLogging(logging => logging.AddFileLogger())
            .ConfigureServices((context, services) =>
            {
                // Register Managers
                services.AddSingleton<IProviderManager, ProviderManager>();
                services.AddSingleton<ISettingsManager, SettingsManager>();
                services.AddSingleton<IClaudeSession, ClaudeSession>();
                services.AddSingleton<IClaudeApiClient, ClaudeApiClient>();
                services.AddSingleton<IOpenCodeSession, OpenCodeSession>();
                services.AddSingleton<IOpenCodeApiClient, OpenCodeApiClient>();

                // Register Providers
                services.AddTransient<IProvider, ClaudeWebProvider>();
                services.AddTransient<IProvider, OpenCodeProvider>();

                // Register ViewModels
                services.AddSingleton<TrayViewModel>();
                services.AddSingleton<WidgetViewModel>();
                services.AddSingleton<SettingsViewModel>();

                // Register Views
                services.AddSingleton<Views.WidgetWindow>();
                services.AddSingleton<Views.SettingsWindow>();
                services.AddTransient<Views.AuthWindow>();
                services.AddTransient<Views.OpenCodeAuthWindow>();
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
            // Load the .ico directly. Going PNG -> GetHicon() loses alpha, producing an
            // opaque tray background on dark taskbars; the .ico preserves transparency.
            var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (iconStream != null)
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream.Stream);
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
        (_host!.Services.GetService<IOpenCodeApiClient>() as IDisposable)?.Dispose();

        await _host!.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
