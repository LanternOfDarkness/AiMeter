using System;
using System.Collections.Generic;

namespace AiMeter.Tests.Fakes;

/// <summary>
/// Minimal IServiceProvider for constructing real ViewModels in tests without spinning
/// up WPF windows. Returns null for every service (the resync path never resolves one).
/// </summary>
public sealed class FakeServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object?> _overrides = new();

    public FakeServiceProvider With(Type serviceType, object? instance)
    {
        _overrides[serviceType] = instance;
        return this;
    }

    public object? GetService(Type serviceType) =>
        _overrides.TryGetValue(serviceType, out var instance) ? instance : null;
}