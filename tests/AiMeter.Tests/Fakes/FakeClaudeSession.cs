using System.Collections.Generic;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>Minimal IClaudeSession stub (only consulted by AccountAction, not the resync path).</summary>
public sealed class FakeClaudeSession : IClaudeSession
{
    public bool HasSession { get; set; }

    public void Store(IEnumerable<(string Name, string Value)> cookies) { }
    public void Clear() => HasSession = false;
}