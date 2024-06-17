using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Tasks;

namespace Odin.Services.Tenant.BackgroundService;

#nullable enable

public interface ITenantBackgroundServiceManager
{
    Task StartAsync(string serviceIdentifier, AbstractTenantBackgroundService backgroundService);
    Task StopAsync(string serviceIdentifier);
    Task StopAllAsync();
}

//

public sealed class TenantBackgroundServiceManager(ILogger<TenantBackgroundServiceManager> logger, Tenant tenant)
    : ITenantBackgroundServiceManager, IDisposable
{
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly AsyncLock _mutex = new();
    private readonly Dictionary<string, AbstractTenantBackgroundService> _backgroundServices = new();

    //

    public async Task StartAsync(string serviceIdentifier, AbstractTenantBackgroundService backgroundService)
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

        logger.LogDebug("Starting background service '{serviceIdentifier}' for {tenant}", serviceIdentifier, tenant.Name);
        await backgroundService.InternalStartAsync(_stoppingCts.Token);
    }

    //

    public async Task StopAsync(string serviceIdentifier)
    {
        AbstractTenantBackgroundService? backgroundService;
        using (await _mutex.LockAsync())
        {
            _backgroundServices.Remove(serviceIdentifier, out backgroundService);
        }
        if (backgroundService != null)
        {
            logger.LogDebug("Stopping background service '{serviceIdentifier}' for {tenant}", serviceIdentifier, tenant.Name);
            await backgroundService.InternalStopAsync(_stoppingCts.Token);
        }
    }

    //

    public async Task StopAllAsync()
    {
        await _stoppingCts.CancelAsync();

        var tasks = new List<Task>();
        foreach (var serviceIdentifier in _backgroundServices.Keys)
        {
            tasks.Add(StopAsync(serviceIdentifier));
        }
        await Task.WhenAll(tasks);
    }

    //

    public void Dispose()
    {
        StopAllAsync().BlockingWait();
        _stoppingCts.Dispose();
    }
}

