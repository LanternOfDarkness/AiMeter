using System;
using System.Collections.Generic;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>Minimal IOpenCodeSession stub.</summary>
public sealed class FakeOpenCodeSession : IOpenCodeSession
{
    public string ProviderName => "OpenCode";
    public bool HasSession { get; set; }
    public int LoginGeneration { get; set; }
    public string? StatusNote { get; private set; }

    public event EventHandler? StatusNoteChanged;

    public void Store(IEnumerable<(string Name, string Value)> cookies) => LoginGeneration++;
    public void Clear() => HasSession = false;

    public void SetStatusNote(string? note)
    {
        if (note == StatusNote) return;
        StatusNote = note;
        StatusNoteChanged?.Invoke(this, EventArgs.Empty);
    }
}
