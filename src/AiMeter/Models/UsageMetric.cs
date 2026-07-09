namespace AiMeter.Models;

public class UsageMetric
{
    public string Name { get; set; } = string.Empty;
    public double RemainingQuota { get; set; }
    public double TotalQuota { get; set; }
    public DateTime? ResetTime { get; set; }
    public string? ProviderIconUrl { get; set; }
    
    public double RemainingPercentage => TotalQuota > 0 ? (RemainingQuota / TotalQuota) * 100 : 0;
}
