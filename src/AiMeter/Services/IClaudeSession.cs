using System.Collections.Generic;

namespace AiMeter.Services;

public interface IClaudeSession
{
    bool HasSession { get; }

    void Store(IEnumerable<(string Name, string Value)> cookies);
    void Clear();
}
