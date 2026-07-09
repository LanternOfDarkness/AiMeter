namespace AiMeter.Models;

public class UsageMetric
{
    public string Name { get; set; } = string.Empty;
    public double RemainingQuota { get; set; }
    public double TotalQuota { get; set; }
    public DateTime? ResetTime { get; set; }
    public string? ProviderIconUrl { get; set; }
    
    public double RemainingPercentage => TotalQuota > 0 ? (RemainingQuota / TotalQuota) * 100 : 0;

    public string ResetTimeText
    {
        get
        {
            if (!ResetTime.HasValue) return string.Empty;
            var timeSpan = ResetTime.Value - DateTime.Now;
            if (timeSpan.TotalMinutes < 0) return "Resetting...";
            if (timeSpan.TotalHours >= 1)
                return $"Resets in {(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            return $"Resets in {timeSpan.Minutes}m";
        }
    }
}
