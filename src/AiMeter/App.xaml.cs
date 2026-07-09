using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using AiMeter.ViewModels;
using AiMeter.Managers;
using AiMeter.Providers;

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
                // Register Core Services
                services.AddSingleton<IProviderManager, ProviderManager>();
                
                // Register Providers
                services.AddTransient<IProvider, MockProvider>();

                // Register ViewModels
                services.AddSingleton<TrayViewModel>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host!.StartAsync();
        
        // Start monitoring usage
        var providerManager = _host.Services.GetRequiredService<IProviderManager>();
        await providerManager.StartAsync();

        base.OnStartup(e);

        _notifyIcon = (TaskbarIcon)FindResource("TrayIcon");
        if (_notifyIcon != null)
        {
            _notifyIcon.Icon = System.Drawing.SystemIcons.Information;
            _notifyIcon.DataContext = _host.Services.GetRequiredService<TrayViewModel>();
            _notifyIcon.ForceCreate();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();

        await _host!.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
