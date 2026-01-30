using System.Threading.Tasks;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.Background;

#nullable enable

public interface IBackgroundServiceNotifier<T> where T : AbstractBackgroundService
{
    Task NotifyWorkAvailableAsync(string? serviceIdentifier = null);
}

public class BackgroundServiceNotifier<T>(IBackgroundServiceManager  backgroundServiceManager):
    IBackgroundServiceNotifier<T> where T : AbstractBackgroundService
{
    public async Task NotifyWorkAvailableAsync(string? serviceIdentifier = null)
    {
        serviceIdentifier ??= typeof(T).Name;
        await backgroundServiceManager.NotifyWorkAvailableAsync(serviceIdentifier);
    }
}