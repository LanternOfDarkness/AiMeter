namespace AiMeter.Models;

public enum WidgetLayoutMode
{
    Compact,
    Taskbar,

    /// <summary>
    /// Hosts the metric columns inside the Windows 11 taskbar itself (via Deskband11Lib)
    /// instead of a free-floating window. Only available on 24H2 (build 26100)+; see
    /// <see cref="AppConfig.IsDeskbandSupported"/>.
    /// </summary>
    Deskband
}
