using System.Threading.Tasks;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.Background;

#nullable enable

public interface IBackgroundServiceTrigger<T> where T : AbstractBackgroundService
{
    Task PulseBackgroundProcessorAsync(string? serviceIdentifier = null);
}

public class BackgroundServiceTrigger<T>(IBackgroundServiceManager  backgroundServiceManager):
    IBackgroundServiceTrigger<T> where T : AbstractBackgroundService
{
    public async Task PulseBackgroundProcessorAsync(string? serviceIdentifier = null)
    {
        serviceIdentifier ??= typeof(T).Name;
        await backgroundServiceManager.PulseBackgroundProcessorAsync(serviceIdentifier);
    }
}