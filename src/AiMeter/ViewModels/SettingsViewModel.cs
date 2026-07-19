using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Services;
using AiMeter.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AiMeter.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsManager _settingsManager;
    private readonly IProviderManager _providerManager;
    private readonly IStartupManager _startupManager;

    public AppConfig Config => _settingsManager.Current;

    public ObservableCollection<MetricOption> MetricOptions { get; } = new();

    /// <summary>One login/logout row per usage provider (Claude, OpenCode, …).</summary>
    public ObservableCollection<AccountRowViewModel> Accounts { get; } = new();

    /// <summary>
    /// Preset palette for the per-metric color picker. "Auto" (null) reverts a metric to the
    /// quota-threshold palette; the rest are stored as hex in AppConfig.MetricColors. A curated
    /// list avoids pulling in a third-party color-picker control.
    /// </summary>
    public IReadOnlyList<ColorPreset> ColorPresets { get; } = new List<ColorPreset>
    {
        new("Auto (quota)", null),
        new("Green", "#2ECC71"),
        new("Blue", "#3498DB"),
        new("Orange", "#E67E22"),
        new("Red", "#E74C3C"),
        new("Purple", "#9B59B6"),
        new("Teal", "#1ABC9C"),
        new("Pink", "#E84393"),
        new("Yellow", "#F1C40F"),
        new("Gray", "#95A5A6"),
    };

    /// <summary>
    /// Launch-at-Windows-startup toggle. Backed directly by the registry (via
    /// <see cref="IStartupManager"/>), so it applies immediately and always reflects reality —
    /// it isn't part of settings.json and doesn't wait for Save.
    /// </summary>
    public bool LaunchOnStartup
    {
        get => _startupManager.IsEnabled;
        set
        {
            _startupManager.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    public SettingsViewModel(ISettingsManager settingsManager, IProviderManager providerManager,
        IClaudeSession claudeSession, IOpenCodeSession openCodeSession, IStartupManager startupManager,
        IServiceProvider serviceProvider)
    {
        _settingsManager = settingsManager;
        _providerManager = providerManager;
        _startupManager = startupManager;

        Accounts.Add(new AccountRowViewModel(claudeSession,
            () => serviceProvider.GetRequiredService<AuthWindow>(),
            "Requires logging into Claude.ai to fetch web limits."));
        Accounts.Add(new AccountRowViewModel(openCodeSession,
            () => serviceProvider.GetRequiredService<OpenCodeAuthWindow>(),
            "Requires logging into opencode.ai to fetch usage."));

        foreach (var name in _providerManager.KnownMetricNames)
        {
            AddMetricOption(name);
        }

        _providerManager.KnownMetricNames.CollectionChanged += KnownMetricNames_CollectionChanged;
    }

    private void KnownMetricNames_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (string name in e.NewItems)
        {
            AddMetricOption(name);
        }
    }

    private void AddMetricOption(string name)
    {
        if (MetricOptions.Any(o => o.Name == name)) return;

        var isSelected = Config.SelectedMetrics.Count == 0 || Config.SelectedMetrics.Contains(name);
        var label = Config.MetricLabels.TryGetValue(name, out var l) ? l : string.Empty;
        var color = Config.MetricColors.TryGetValue(name, out var c) ? c : null;
        MetricOptions.Add(new MetricOption(name, isSelected, label, color));
    }

    /// <summary>
    /// Re-applies the saved selection to every metric option, discarding any unsaved
    /// checkbox edits. Called on reopen so a singleton VM never shows abandoned toggles
    /// from the last time the window was closed without Save.
    /// </summary>
    public void ResyncMetricSelections()
    {
        foreach (var option in MetricOptions)
        {
            option.IsSelected = Config.SelectedMetrics.Count == 0 || Config.SelectedMetrics.Contains(option.Name);
            option.CustomLabel = Config.MetricLabels.TryGetValue(option.Name, out var l) ? l : string.Empty;
            option.CustomColor = Config.MetricColors.TryGetValue(option.Name, out var c) ? c : null;
        }
    }

    [RelayCommand]
    private void Save(Window window)
    {
        Config.SelectedMetrics = MetricOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();

        // Persist only the non-empty per-metric overrides, keyed by metric name (the same key
        // ProviderManager.ApplyUserCustomizations reads back).
        Config.MetricLabels = MetricOptions
            .Where(o => !string.IsNullOrWhiteSpace(o.CustomLabel))
            .ToDictionary(o => o.Name, o => o.CustomLabel.Trim());
        Config.MetricColors = MetricOptions
            .Where(o => !string.IsNullOrWhiteSpace(o.CustomColor))
            .ToDictionary(o => o.Name, o => o.CustomColor!);

        _settingsManager.Save();
        // Re-filter from cache so the widget reflects the new selection/labels/colors instantly
        // instead of waiting for the next poll tick. No network call.
        _providerManager.RefilterMetrics();
        // The settings window is a singleton: never Close() it (that disposes it), just hide.
        window?.Hide();
    }
}
