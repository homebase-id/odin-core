using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Tasks;
using Odin.Services.Background.Services;

namespace Odin.Services.Background;

#nullable enable

public interface IBackgroundServiceManager
{
    Task StartAsync(string serviceIdentifier, AbstractBackgroundService backgroundService);
    Task StopAsync(string serviceIdentifier);
    Task StopAllAsync();
    Task ShutdownAsync();
}

//

public sealed class BackgroundServiceManager(ILogger<BackgroundServiceManager> logger, string owner)
    : IBackgroundServiceManager, IDisposable
{
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly AsyncLock _mutex = new();
    private readonly Dictionary<string, AbstractBackgroundService> _backgroundServices = new();

    //

    public async Task StartAsync(string serviceIdentifier, AbstractBackgroundService backgroundService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceIdentifier);

        if (_stoppingCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("The background service is stopping.");
        }

        using (await _mutex.LockAsync())
        {
            if (!_backgroundServices.TryAdd(serviceIdentifier, backgroundService))
            {
                return; // already running
            }
        }

        logger.LogInformation("Starting background service '{serviceIdentifier}' for {owner}", serviceIdentifier, owner);
        await backgroundService.InternalStartAsync(_stoppingCts.Token);
    }

    //

    public async Task StopAsync(string serviceIdentifier)
    {
        AbstractBackgroundService? backgroundService;
        using (await _mutex.LockAsync())
        {
            _backgroundServices.Remove(serviceIdentifier, out backgroundService);
        }
        if (backgroundService != null)
        {
            logger.LogInformation("Stopping background service '{serviceIdentifier}' for {owner}", serviceIdentifier, owner);
            await backgroundService.InternalStopAsync(_stoppingCts.Token);
            
            // SEB:NOTE
            // Since BackgroundServiceManager did not create the background service, it is not responsible for disposing it. 
        }
    }

    //

    public async Task StopAllAsync()
    {
        var tasks = new List<Task>();
        using (await _mutex.LockAsync())
        {
            foreach (var serviceIdentifier in _backgroundServices.Keys)
            {
                tasks.Add(StopAsync(serviceIdentifier));
            }
        }
        await Task.WhenAll(tasks);
    }

    //

    public async Task ShutdownAsync()
    {
        await _stoppingCts.CancelAsync();
        await StopAllAsync();
    }

    //

    public void Dispose()
    {
        ShutdownAsync().BlockingWait();
        _stoppingCts.Dispose();
    }
}

