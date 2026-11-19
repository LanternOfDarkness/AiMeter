using System.Windows;
using System.Windows.Threading;

namespace AiMeter.Services.Updates;

/// <summary>
/// <see cref="IUiDispatcher"/> over the application dispatcher. Unit tests have no
/// <see cref="Application"/>, so posted actions run inline there instead of being lost.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    public bool IsOnDispatcherThread =>
        Application.Current?.Dispatcher.CheckAccess() ?? true;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }
}
