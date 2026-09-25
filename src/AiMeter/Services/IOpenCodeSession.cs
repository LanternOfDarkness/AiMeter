using System.Collections.Generic;

namespace AiMeter.Services;

public interface IOpenCodeSession : IProviderSession
{
    void Store(IEnumerable<(string Name, string Value)> cookies);

    /// <summary>Incremented on every Store, so the provider can drop back-offs after a fresh login.</summary>
    int LoginGeneration { get; }

    /// <summary>Sets <see cref="IProviderSession.StatusNote"/> (raising StatusNoteChanged if it changed).</summary>
    void SetStatusNote(string? note);
}
