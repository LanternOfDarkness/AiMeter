using System;
using System.IO;

namespace AiMeter.Logging;

public static class LogPaths
{
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiMeter", "logs");
}
