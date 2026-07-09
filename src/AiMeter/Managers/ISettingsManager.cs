using AiMeter.Models;

namespace AiMeter.Managers;

public interface ISettingsManager
{
    AppConfig Current { get; }
    void Load();
    void Save();
}
