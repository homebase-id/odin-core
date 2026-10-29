using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Tasks;
using Odin.Core.Util;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.Background;

#nullable enable

public interface IBackgroundServiceManager
{
    T Create<T>(string? serviceIdentifier = null) where T : AbstractBackgroundService;
    Task StartAsync(AbstractBackgroundService service);
    Task<T> StartAsync<T>(string? serviceIdentifier = null) where T : AbstractBackgroundService;
    Task StopAsync(string serviceIdentifier);
    Task StopAsync<T>();
    Task StopAllAsync();
    Task ShutdownAsync();
    Task NotifyWorkAvailableAsync(string serviceIdentifier);
    Task NotifyWorkAvailableAsync<T>();

    /// <summary>
    /// True while any service is registered or still stopping. Becomes false only once every
    /// stop has completed, so no service is doing work anymore.
    /// </summary>
    bool IsRunning { get; }
}

//

public sealed class BackgroundServiceManager(ILifetimeScope lifetimeScope, string owner)
    : IBackgroundServiceManager, IDisposable
{
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly AsyncReaderWriterLock _lock = new();
    private readonly Dictionary<string, ScopedAbstractBackgroundService> _backgroundServices = new();
    private readonly string _correlationId = Guid.NewGuid().ToString();
    // Services notified while not registered: not started yet (a tenant's services start before the system ones),
    // or stopped on purpose. Each is woken as soon as it is created again, so the wake-up is not lost.
    private readonly HashSet<string> _pendingWakeUps = new();
    // Services removed from _backgroundServices whose stop has not completed yet
    private int _stoppingCount;
    private readonly ILogger<BackgroundServiceManager> _logger = lifetimeScope.Resolve<ILogger<BackgroundServiceManager>>();
    private bool _disposed;

    //
    
    private record ScopedAbstractBackgroundService(ILifetimeScope Scope, AbstractBackgroundService BackgroundService);

    //

    public bool IsRunning
    {
        get
        {
            using (_lock.ReaderLock())
            {
                return _backgroundServices.Count > 0 || Volatile.Read(ref _stoppingCount) > 0;
            }
        }
    }

    //

    public T Create<T>(string? serviceIdentifier = null) where T : AbstractBackgroundService
    {
        serviceIdentifier ??= typeof(T).Name;
        
        if (_stoppingCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("The background service manager is stopping.");
        }
        
        using (_lock.WriterLock())
        {
            if (_backgroundServices.ContainsKey(serviceIdentifier))
            {
                throw new InvalidOperationException($"Background service '{serviceIdentifier}' already exists.");
            }

            var serviceScope = lifetimeScope.BeginLifetimeScope($"{serviceIdentifier}:{Guid.NewGuid()}");
            var backgroundService = serviceScope.Resolve<T>();
            if (_pendingWakeUps.Remove(serviceIdentifier))
            {
                // The wake-up event stays set until the service's first sleep, which then returns at once
                backgroundService.InternalNotifyWorkAvailable();
            }
            var scopedService = new ScopedAbstractBackgroundService(serviceScope, backgroundService);
            _backgroundServices.Add(serviceIdentifier, scopedService);           
            
            return backgroundService;
        }
    }
    
    //

    public async Task StartAsync(AbstractBackgroundService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        UpdateLogContext();
        
        if (_stoppingCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("The background service manager is stopping.");
        }

        using (await _lock.ReaderLockAsync())
        {
            var (serviceIdentifier, scopedService) = 
                _backgroundServices.FirstOrDefault(x => ReferenceEquals(x.Value.BackgroundService, service));

            if (serviceIdentifier == default || scopedService == default)
            {
                throw new InvalidOperationException("Background service not found. Did you forget to call Create?");
            }
            
            var correlationIdGenerator = scopedService.Scope.Resolve<ICorrelationIdGenerator>();
            var newCorrelationId = correlationIdGenerator.Generate();

            _logger.LogInformation("Starting background service '{serviceIdentifier}' with new correlation-id {cid}",
                serviceIdentifier, newCorrelationId);

            var correlationContext = scopedService.Scope.Resolve<ICorrelationContext>();
            correlationContext.Id = newCorrelationId;

            await scopedService.BackgroundService.InternalStartAsync(_stoppingCts.Token);
        }
    }
    
    //
    
    public async Task<T> StartAsync<T>(string? serviceIdentifier = null) where T : AbstractBackgroundService
    {
        serviceIdentifier ??= typeof(T).Name;
        var service = Create<T>(serviceIdentifier);
        await StartAsync(service);
        return service;
    }

    //

    public async Task StopAsync(string serviceIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceIdentifier);

        UpdateLogContext();

        ScopedAbstractBackgroundService? scopedAbstractBackgroundService;
        using (await _lock.WriterLockAsync())
        {
            if (_backgroundServices.Remove(serviceIdentifier, out scopedAbstractBackgroundService))
            {
                // Counted under the same lock, so IsRunning never sees the service as gone before its stop completes
                Interlocked.Increment(ref _stoppingCount);
            }
        }
        if (scopedAbstractBackgroundService != null)
        {
            try
            {
                _logger.LogInformation("Stopping background service '{serviceIdentifier}'", serviceIdentifier);
                await scopedAbstractBackgroundService.BackgroundService.InternalStopAsync(_stoppingCts.Token);
                scopedAbstractBackgroundService.Scope.Dispose();
                _logger.LogInformation("Stopped background service '{serviceIdentifier}'", serviceIdentifier);
            }
            finally
            {
                Interlocked.Decrement(ref _stoppingCount);
            }
        }
    }

    //

    public Task StopAsync<T>()
    {
        return StopAsync(typeof(T).Name);
    }

    //

    public async Task StopAllAsync()
    {
        List<string> identifiers;
        using (await _lock.ReaderLockAsync())
        {
            identifiers = _backgroundServices.Keys.ToList();
        }
        await Task.WhenAll(identifiers.Select(StopAsync));
    }

    //

    public async Task ShutdownAsync()
    {
        await _stoppingCts.CancelAsync();
        await StopAllAsync();
    }

    //

    public async Task NotifyWorkAvailableAsync(string serviceIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceIdentifier);

        if (_stoppingCts.IsCancellationRequested)
        {
            return;
        }

        using (await _lock.ReaderLockAsync())
        {
            if (_backgroundServices.TryGetValue(serviceIdentifier, out var backgroundService))
            {
                backgroundService.BackgroundService.InternalNotifyWorkAvailable();
                return;
            }
        }

        // Not registered: record it for Create rather than wait for it, so a caller never blocks on a service
        // that starts later or not at all
        using (await _lock.WriterLockAsync())
        {
            if (_backgroundServices.TryGetValue(serviceIdentifier, out var backgroundService))
            {
                backgroundService.BackgroundService.InternalNotifyWorkAvailable();
            }
            else
            {
                _pendingWakeUps.Add(serviceIdentifier);
            }
        }
    }

    //

    public Task NotifyWorkAvailableAsync<T>()
    {
        return NotifyWorkAvailableAsync(typeof(T).Name);
    }

    //

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ShutdownAsync().BlockingWait();
            _stoppingCts.Dispose();
        }
    }
    
    //
    
    // This makes sure that we get a new per-tenant correlation-id
    // and that the correlation-id's sticky hostname is re-applied when
    // stopping the services from at different async context
    private void UpdateLogContext()
    {
        var correlationIdContext = lifetimeScope.Resolve<ICorrelationContext>();
        correlationIdContext.Id = _correlationId;
        var stickyHostnameContext = lifetimeScope.Resolve<IStickyHostname>();
        stickyHostnameContext.Hostname = $"{owner}&"; // "&": hat-tip to 1977 Bourne shell background job syntax 
    }
}

