using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiMeter.Models;
using Microsoft.Extensions.Logging;

namespace AiMeter.Services;

/// <summary>
/// Versioned JSON snapshot cache stored at <c>%AppData%\AiMeter\cache.json</c>. Only the
/// fetched numbers and timestamps are persisted - display names, user colors and staleness
/// are app state and are never written. Writes go to a temp file first and are then moved
/// over the real file, so a crash cannot leave a truncated cache behind.
/// </summary>
public sealed class FileMetricCacheStore : IMetricCacheStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly ILogger<FileMetricCacheStore>? _logger;
    private readonly Dictionary<string, CachedProviderMetrics> _entries = new(StringComparer.Ordinal);

    public FileMetricCacheStore(ILogger<FileMetricCacheStore>? logger = null)
        : this(DefaultFilePath(), logger)
    {
    }

    public FileMetricCacheStore(string filePath, ILogger<FileMetricCacheStore>? logger = null)
    {
        _filePath = filePath;
        _logger = logger;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not create cache directory {Directory}", directory);
            }
        }
    }

    private static string DefaultFilePath()
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataFolder, "AiMeter", "cache.json");
    }

    public bool TryGet(string providerName, out CachedProviderMetrics snapshot)
    {
        if (_entries.TryGetValue(providerName, out var found))
        {
            snapshot = found;
            return true;
        }

        snapshot = null!;
        return false;
    }

    public void Set(string providerName, IReadOnlyList<UsageMetric> metrics, DateTime fetchedAtUtc)
    {
        _entries[providerName] = new CachedProviderMetrics(metrics, fetchedAtUtc);
    }

    public void Remove(string providerName) => _entries.Remove(providerName);

    public void Save()
    {
        try
        {
            var document = new CacheDocument
            {
                Version = CurrentVersion,
                Providers = _entries.ToDictionary(
                    pair => pair.Key,
                    pair => new CachedProviderEntry
                    {
                        FetchedAtUtc = pair.Value.FetchedAtUtc,
                        Metrics = pair.Value.Metrics.Select(ToCachedMetric).ToList(),
                    },
                    StringComparer.Ordinal),
            };

            var json = JsonSerializer.Serialize(document, JsonOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving metric cache to {Path}", _filePath);
        }
    }

    public void Load()
    {
        _entries.Clear();

        try
        {
            if (!File.Exists(_filePath)) return;

            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<CacheDocument>(json, JsonOptions);

            if (document is null)
            {
                _logger?.LogWarning("Metric cache at {Path} was empty; starting with no cached values", _filePath);
                return;
            }

            if (document.Version != CurrentVersion)
            {
                _logger?.LogWarning(
                    "Metric cache at {Path} has unsupported version {Version}; starting with no cached values",
                    _filePath,
                    document.Version);
                return;
            }

            foreach (var pair in document.Providers)
            {
                var metrics = pair.Value.Metrics.Select(ToUsageMetric).ToList();
                if (metrics.Count == 0) continue;

                _entries[pair.Key] = new CachedProviderMetrics(metrics, pair.Value.FetchedAtUtc);
            }
        }
        catch (Exception ex)
        {
            _entries.Clear();
            _logger?.LogError(ex, "Error loading metric cache from {Path}; starting with no cached values", _filePath);
        }
    }

    private static CachedMetric ToCachedMetric(UsageMetric metric) => new()
    {
        Name = metric.Name,
        TotalQuota = metric.TotalQuota,
        RemainingQuota = metric.RemainingQuota,
        ResetTime = metric.ResetTime,
        WindowDuration = metric.WindowDuration,
        IsMoneyMetric = metric.IsMoneyMetric,
        IsUnbounded = metric.IsUnbounded,
        UsedAmount = metric.UsedAmount,
        LimitAmount = metric.LimitAmount,
        BalanceAmount = metric.BalanceAmount,
        Currency = metric.Currency,
    };

    private static UsageMetric ToUsageMetric(CachedMetric metric) => new()
    {
        Name = metric.Name,
        TotalQuota = metric.TotalQuota,
        RemainingQuota = metric.RemainingQuota,
        ResetTime = metric.ResetTime,
        WindowDuration = metric.WindowDuration,
        IsMoneyMetric = metric.IsMoneyMetric,
        IsUnbounded = metric.IsUnbounded,
        UsedAmount = metric.UsedAmount,
        LimitAmount = metric.LimitAmount,
        BalanceAmount = metric.BalanceAmount,
        Currency = metric.Currency,
    };

    private sealed class CacheDocument
    {
        public int Version { get; set; }
        public Dictionary<string, CachedProviderEntry> Providers { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class CachedProviderEntry
    {
        public DateTime FetchedAtUtc { get; set; }
        public List<CachedMetric> Metrics { get; set; } = new();
    }

    private sealed class CachedMetric
    {
        public string Name { get; set; } = string.Empty;
        public double TotalQuota { get; set; }
        public double RemainingQuota { get; set; }
        public DateTime? ResetTime { get; set; }
        public TimeSpan? WindowDuration { get; set; }
        public bool IsMoneyMetric { get; set; }
        public bool IsUnbounded { get; set; }
        public double? UsedAmount { get; set; }
        public double? LimitAmount { get; set; }
        public double? BalanceAmount { get; set; }
        public string? Currency { get; set; }
    }
}
