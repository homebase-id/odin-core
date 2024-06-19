using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Odin.Services.Tenant.BackgroundService;

#nullable enable

public abstract class AbstractTenantBackgroundService
{
    private readonly AsyncAutoResetEvent _pulseEvent = new();
    private CancellationTokenSource? _stoppingCts;
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
        try
        {
            var pulse = _pulseEvent.WaitAsync(stoppingToken);
            var delay = Task.Delay(duration, stoppingToken);
            await Task.WhenAny(pulse, delay);
        }
        catch (OperationCanceledException)
        {
            // ignore - this is expected and will happen when stoppingToken is cancelled
        }
    }

    //

    // Call me from anywhere to wake up the service from SleepAsync
    public void Pulse()
    {
        _pulseEvent.Set();
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

