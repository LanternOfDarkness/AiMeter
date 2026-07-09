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
        var random = new Random();
        var metrics = new List<UsageMetric>
        {
            new UsageMetric
            {
                Name = "Claude Messages",
                TotalQuota = 100,
                RemainingQuota = random.Next(0, 101),
                ResetTime = DateTime.Now.AddHours(5)
            },
            new UsageMetric
            {
                Name = "Claude Max Usage",
                TotalQuota = 50,
                RemainingQuota = random.Next(40, 51),
                ResetTime = DateTime.Now.AddDays(7)
            },
            new UsageMetric
            {
                Name = "GPT-4",
                TotalQuota = 1000,
                RemainingQuota = random.Next(100, 1001),
                ResetTime = DateTime.Now.AddHours(4)
            }
        };

        return Task.FromResult<IReadOnlyList<UsageMetric>>(metrics);
    }
}
