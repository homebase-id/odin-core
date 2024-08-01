using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
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

public sealed class BackgroundServiceManager(IServiceProvider services, string owner) : IBackgroundServiceManager, IDisposable
{
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly AsyncLock _mutex = new();
    private readonly Dictionary<string, AbstractBackgroundService> _backgroundServices = new();
    private readonly string _correlationId = Guid.NewGuid().ToString();
    private readonly ILogger<BackgroundServiceManager> _logger = services.GetRequiredService<ILogger<BackgroundServiceManager>>();

    //

    public async Task StartAsync(string serviceIdentifier, AbstractBackgroundService backgroundService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceIdentifier);

        UpdateLogContext();
        
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

        _logger.LogInformation("Starting background service '{serviceIdentifier}'", serviceIdentifier);
        await backgroundService.InternalStartAsync(_stoppingCts.Token);
    }

    //

    public async Task StopAsync(string serviceIdentifier)
    {
        UpdateLogContext();
        
        AbstractBackgroundService? backgroundService;
        using (await _mutex.LockAsync())
        {
            _backgroundServices.Remove(serviceIdentifier, out backgroundService);
        }
        if (backgroundService != null)
        {
            _logger.LogInformation("Stopping background service '{serviceIdentifier}'", serviceIdentifier);
            await backgroundService.InternalStopAsync(_stoppingCts.Token);
            _logger.LogInformation("Stopped background service '{serviceIdentifier}'", serviceIdentifier);
            
            // SEB:NOTE
            // Since BackgroundServiceManager did not create the background service,
            // it is not responsible for disposing it.
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
        
        // SEB:NOTE
        // Since BackgroundServiceManager did not create the background service,
        // it is not responsible for disposing it.
    }
    
    //
    
    // This makes sure that we get a new per-tenant correlation-id
    // and that the correlation-id can sticky hostname is re-applied when
    // stopping the services from at different async context
    private void UpdateLogContext()
    {
        var correlationIdContext = services.GetRequiredService<ICorrelationContext>();
        correlationIdContext.Id = _correlationId;
        var stickyHostnameContext = services.GetRequiredService<IStickyHostname>();
        stickyHostnameContext.Hostname = $"{owner}&"; // "&": hat-tip to 1977 Bourne shell background job syntax 
    }
}

