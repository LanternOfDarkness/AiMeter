using System.Collections.Generic;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>Minimal IOpenCodeSession stub (only consulted by the account rows, not the resync path).</summary>
public sealed class FakeOpenCodeSession : IOpenCodeSession
{
    public string ProviderName => "OpenCode";
    public bool HasSession { get; set; }

    public void Store(IEnumerable<(string Name, string Value)> cookies) { }
    public void Clear() => HasSession = false;
}
