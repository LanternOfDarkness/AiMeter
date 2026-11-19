using System;

namespace AiMeter.Services.Updates;

/// <summary>Notification dedupe and last-check timestamps persisted outside <c>settings.json</c>.</summary>
public sealed class UpdateState
{
    /// <summary>Version the user skipped; suppressed until a newer one appears.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Version of the last update notification raised.</summary>
    public string? LastNotifiedVersion { get; set; }

    /// <summary>When the last notification was raised or dismissed with <c>Later</c>.</summary>
    public DateTimeOffset? LastNotifiedUtc { get; set; }

    /// <summary>When the last successful update check completed.</summary>
    public DateTimeOffset? LastCheckUtc { get; set; }
}
