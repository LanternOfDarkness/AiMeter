namespace AiMeter.Models;

/// <summary>
/// One entry in the per-metric color picker: a friendly name plus the hex value stored in
/// AppConfig.MetricColors. A null <see cref="Value"/> means "Auto" — fall back to the
/// quota-threshold palette.
/// </summary>
public record ColorPreset(string Name, string? Value);
