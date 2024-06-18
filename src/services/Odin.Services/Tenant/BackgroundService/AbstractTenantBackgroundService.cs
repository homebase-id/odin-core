using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;

namespace Odin.Services.Tenant.BackgroundService;

#nullable enable

public abstract class AbstractTenantBackgroundService(Tenant tenant)
{
    protected readonly OdinContext OdinContext = new()
    {
        Tenant = (OdinId)tenant.Name,
        Caller = new CallerContext(default, null, SecurityGroupType.Anonymous)
    };

    private readonly AsyncLock _mutex = new();
    private CancellationTokenSource? _stoppingCts;
    private CancellationTokenSource? _wakeUpCts;
    private Task? _task;

    // Override initialization logic here. TenantBackgroundServiceManager will wait for this to complete before starting the service.
    public virtual Task StartingAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    // Implement me to do your work
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    //

    // Override cleanup logic here. TenantBackgroundServiceManager will run this after the service has stopped.
    public virtual Task StoppedAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    //

    // Call me in your ExecuteAsync method to sleep for a while
    protected async Task SleepAsync(TimeSpan duration, CancellationToken stoppingToken)
    {
        lock (_mutex)
        {
            if (_wakeUpCts != null)
            {
                return;
            }
            _wakeUpCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }

        try
        {
            await Task.Delay(duration, _wakeUpCts.Token);
        }
        catch (OperationCanceledException)
        {
            // ignore - this will happen when _wakeUpCts is signalled/cancelled
        }
        finally
        {
            lock (_mutex)
            {
                _wakeUpCts?.Dispose();
                _wakeUpCts = null;
            }
        }
    }

    //

    // Call me from anywhere to wake up the service from SleepAsync
    public void Pulse()
    {
        lock (_mutex)
        {
            _wakeUpCts?.Cancel();
        }
    }

    //

    internal async Task InternalStartAsync(CancellationToken stoppingToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await StartingAsync(_stoppingCts.Token);

        // No 'await' here, this is intentional; we want to start the task and return immediately
        _task = ExecuteAsync(_stoppingCts.Token);
    }

    //

    internal async Task InternalStopAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_stoppingCts != null)
            {
                await _stoppingCts.CancelAsync();
            }

            if (_task != null)
            {
                try
                {
                    await _task;
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
            }

            await StoppedAsync(stoppingToken);
        }
        finally
        {
            _stoppingCts?.Dispose();
            _stoppingCts = null;
            _task = null;
        }
    }

    //

}

