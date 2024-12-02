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
using Odin.Services.Background.Services;

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
    void PulseBackgroundProcessor(string serviceIdentifier);
    void PulseBackgroundProcessor<T>();
}

//

public sealed class BackgroundServiceManager(ILifetimeScope lifetimeScope, string owner)
    : IBackgroundServiceManager, IDisposable
{
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly AsyncReaderWriterLock _lock = new();
    private readonly Dictionary<string, ScopedAbstractBackgroundService> _backgroundServices = new();
    private readonly string _correlationId = Guid.NewGuid().ToString();
    private readonly ILogger<BackgroundServiceManager> _logger = lifetimeScope.Resolve<ILogger<BackgroundServiceManager>>();
    private bool _disposed;

    //
    
    private record ScopedAbstractBackgroundService(ILifetimeScope Scope, AbstractBackgroundService BackgroundService);
    
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

            var serviceScope = lifetimeScope.BeginLifetimeScope();
            var backgroundService = serviceScope.Resolve<T>();
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
            
            _logger.LogInformation("Starting background service '{serviceIdentifier}'", serviceIdentifier);
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
            _backgroundServices.Remove(serviceIdentifier, out scopedAbstractBackgroundService);
        }
        if (scopedAbstractBackgroundService != null)
        {
            _logger.LogInformation("Stopping background service '{serviceIdentifier}'", serviceIdentifier);
            await scopedAbstractBackgroundService.BackgroundService.InternalStopAsync(_stoppingCts.Token);
            scopedAbstractBackgroundService.Scope.Dispose();
            _logger.LogInformation("Stopped background service '{serviceIdentifier}'", serviceIdentifier);
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
        var tasks = new List<Task>();
        using (await _lock.ReaderLockAsync())
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

    public void PulseBackgroundProcessor(string serviceIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceIdentifier);

        ScopedAbstractBackgroundService? backgroundService;
        using (_lock.ReaderLock())
        {
            _backgroundServices.TryGetValue(serviceIdentifier, out backgroundService);
        }

        // This fixes a race condition during startup where one background service can pulse
        // another background service that hasn't started yet.
        if (backgroundService == null)
        {
            TryRetry.WithDelay(30, TimeSpan.FromSeconds(1), _stoppingCts.Token, () =>
            {
                using (_lock.ReaderLock())
                {
                    if (!_backgroundServices.TryGetValue(serviceIdentifier, out backgroundService))
                    {
                        throw new InvalidOperationException($"Background service '{serviceIdentifier}' not found. Did you forget to start it?");
                    }
                }
            });
        }

        if (!_stoppingCts.IsCancellationRequested)
        {
            backgroundService?.BackgroundService.InternalPulseBackgroundProcessor();
        }
    }

    //

    public void PulseBackgroundProcessor<T>()
    {
        PulseBackgroundProcessor(typeof(T).Name);
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

