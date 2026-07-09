using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Models;

namespace AiMeter.Providers;

public interface IProvider
{
    string Name { get; }
    Task<IReadOnlyList<UsageMetric>> GetMetricsAsync();
}
