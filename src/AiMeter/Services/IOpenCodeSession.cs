using System.Collections.Generic;

namespace AiMeter.Services;

public interface IOpenCodeSession : IProviderSession
{
    void Store(IEnumerable<(string Name, string Value)> cookies);
}
