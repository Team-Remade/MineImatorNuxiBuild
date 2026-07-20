using MineImatorSimplyRemade.core.audio;

namespace MineImatorSimplyRemade.core;

public static class Services
{
    public static AudioEngine AudioEngine { get; private set; }

    public static void Initialize()
    {
        AudioEngine = new AudioEngine();
    }

    public static void Shutdown()
    {
        AudioEngine?.Dispose();
        AudioEngine = null;
    }
}