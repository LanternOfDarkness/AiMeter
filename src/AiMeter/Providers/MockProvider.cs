using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Models;

namespace AiMeter.Providers;

public class MockProvider : IProvider
{
    public string Name => "Mock Provider";

    public Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        var _random = new Random();
        var metrics = new List<UsageMetric>
        {
            new UsageMetric
            {
                Name = "Claude Messages",
                TotalQuota = 100,
                RemainingQuota = _random.Next(0, 100),
                ResetTime = DateTime.Now.AddHours(4).AddMinutes(15)
            },
            new UsageMetric
            {
                Name = "Claude Max",
                TotalQuota = 50,
                RemainingQuota = _random.Next(0, 50),
                ResetTime = DateTime.Now.AddMinutes(45)
            },
            new UsageMetric
            {
                Name = "GPT-4",
                TotalQuota = 500,
                RemainingQuota = _random.Next(0, 500),
                ResetTime = DateTime.Now.AddHours(2)
            }
        };

        return Task.FromResult<IReadOnlyList<UsageMetric>>(metrics);
    }
}
