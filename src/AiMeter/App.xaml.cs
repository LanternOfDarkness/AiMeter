using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using AiMeter.ViewModels;
using AiMeter.Managers;
using AiMeter.Providers;
using AiMeter.Services;
using AiMeter.Services.Updates;

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
                // Register Services
                services.AddSingleton<IMetricCacheStore, FileMetricCacheStore>();
                services.AddSingleton<TimeProvider>(TimeProvider.System);
                services.AddSingleton<IReleaseFeed, GithubReleaseFeed>();
                services.AddSingleton<IUpdateStateStore, FileUpdateStateStore>();
                services.AddSingleton<IUpdateDownloader, UpdateDownloader>();
                services.AddSingleton<IProcessLauncher, ProcessLauncher>();
                services.AddSingleton<IUpdateInstaller, UpdateInstaller>();
                services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();

                // Register Managers
                services.AddSingleton<IProviderManager, ProviderManager>();
                services.AddSingleton<IUpdateManager, UpdateManager>();
                services.AddSingleton<ISettingsManager, SettingsManager>();
                services.AddSingleton<IStartupManager, StartupManager>();
                services.AddSingleton<IClaudeSession, ClaudeSession>();
                services.AddSingleton<IClaudeApiClient, ClaudeApiClient>();
                services.AddSingleton<IOpenCodeSession, OpenCodeSession>();
                services.AddSingleton<IOpenCodeApiClient, OpenCodeApiClient>();
                services.AddSingleton<WidgetHostManager>();

                // Register Providers
                services.AddTransient<IProvider, ClaudeWebProvider>();
                services.AddTransient<IProvider, OpenCodeProvider>();

                // Register ViewModels
                services.AddSingleton<TrayViewModel>();
                services.AddSingleton<WidgetViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<UpdateViewModel>();
                services.AddTransient<UpdateBalloonViewModel>();

                // Register Views
                services.AddSingleton<Views.WidgetWindow>();
                services.AddSingleton<Views.SettingsWindow>();
                services.AddSingleton<Views.UpdateWindow>();
                services.AddTransient<Views.AuthWindow>();
                services.AddTransient<Views.OpenCodeAuthWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (TryGetRestartedFromPid(e.Args) is int previousPid)
        {
            await WaitForPreviousInstanceExitAsync(previousPid);
        }

        await _host!.StartAsync();

        // Start monitoring usage
        var providerManager = _host.Services.GetRequiredService<IProviderManager>();
        await providerManager.StartAsync();

        // Apply the persisted layout mode: shows the floating widget, or attaches the
        // deskband host when the saved mode is Deskband.
        _host.Services.GetRequiredService<WidgetHostManager>().Initialize();

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
            _notifyIcon.Visibility = Visibility.Visible;
            try
            {
                _notifyIcon.ForceCreate();
            }
            catch
            {
                await Task.Delay(500);
                try { _notifyIcon.ForceCreate(); } catch { }
            }
        }

        var updateManager = _host.Services.GetRequiredService<IUpdateManager>();
        var uiDispatcher = _host.Services.GetRequiredService<IUiDispatcher>();
        updateManager.UpdateAvailable += (_, release) =>
        {
            // The manager raises this from its background loop; the balloon needs the UI thread.
            uiDispatcher.Post(() => ShowUpdateBalloon(release));
        };

        await updateManager.StartAsync();

        providerManager.AlertRaised += (s, alert) =>
        {
            _notifyIcon?.ShowNotification(alert.Title, alert.Message, NotificationIcon.Info);
        };

        // Fire-and-forget is safe: CleanupOldExecutablesAsync catches everything it throws.
        _ = CleanupOldExecutablesAsync(_host.Services.GetService<ILogger<App>>());
    }

    private void ShowUpdateBalloon(ReleaseInfo release)
    {
        try
        {
            var vm = _host!.Services.GetRequiredService<UpdateBalloonViewModel>();
            vm.Initialize(release, close: () => _notifyIcon?.CloseBalloon());
            _notifyIcon?.ShowCustomBalloon(
                new Views.UpdateBalloonView { DataContext = vm },
                PopupAnimation.Slide,
                20000);
        }
        catch (Exception ex)
        {
            // The tray menu entry is the fallback; never send a second notification.
            _host?.Services.GetService<ILogger<App>>()
                ?.LogWarning(ex, "Could not show the update tray balloon.");
        }
    }

    private static int? TryGetRestartedFromPid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--restarted-from", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    private async Task WaitForPreviousInstanceExitAsync(int pid)
    {
        var logger = _host!.Services.GetService<ILogger<App>>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Could not query the previous AiMeter process {Pid}.", pid);
            }

            await Task.Delay(250);
        }

        logger?.LogWarning("Timed out waiting for the previous AiMeter process {Pid} to exit.", pid);
    }

    private static async Task CleanupOldExecutablesAsync(ILogger? logger)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            var target = Environment.ProcessPath;
            var directory = string.IsNullOrEmpty(target) ? null : Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var leftovers = new List<string> { target + ".old" };
            try
            {
                leftovers.AddRange(Directory.EnumerateFiles(directory, Path.GetFileName(target) + ".old-*"));
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Could not enumerate update backups in {Directory}.", directory);
            }

            foreach (var path in leftovers)
            {
                for (var attempt = 1; attempt <= 10 && File.Exists(path); attempt++)
                {
                    try
                    {
                        File.Delete(path);
                        logger?.LogDebug("Deleted the leftover update backup {Path}.", path);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (attempt == 10)
                        {
                            logger?.LogWarning(ex, "Could not delete the leftover update backup {Path}.", path);
                            break;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Update backup cleanup stopped early.");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();

        _host!.Services.GetService<WidgetHostManager>()?.Dispose();

        (_host!.Services.GetService<IClaudeApiClient>() as IDisposable)?.Dispose();
        (_host!.Services.GetService<IOpenCodeApiClient>() as IDisposable)?.Dispose();

        await (_host!.Services.GetService<IUpdateManager>()?.StopAsync() ?? Task.CompletedTask);

        await _host!.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
