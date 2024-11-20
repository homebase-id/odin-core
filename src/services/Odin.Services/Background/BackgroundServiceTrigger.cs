using Odin.Services.Background.Services;

namespace Odin.Services.Background;

#nullable enable

public interface IBackgroundServiceTrigger<T> where T : AbstractBackgroundService
{
    void PulseBackgroundProcessor(string? serviceIdentifier = null);
}

public class BackgroundServiceTrigger<T>(IBackgroundServiceManager  backgroundServiceManager):
    IBackgroundServiceTrigger<T> where T : AbstractBackgroundService
{
    public void PulseBackgroundProcessor(string? serviceIdentifier = null)
    {
        serviceIdentifier ??= typeof(T).Name;
        backgroundServiceManager.PulseBackgroundProcessor(serviceIdentifier);
    }
}