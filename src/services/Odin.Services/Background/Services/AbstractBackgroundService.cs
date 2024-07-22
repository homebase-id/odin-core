using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Odin.Services.Background.Services;

#nullable enable

public abstract class AbstractBackgroundService
{
    private static readonly Random Random = new();
    private readonly AsyncManualResetEvent _wakeUpEvent = new();
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

    // Call me in your ExecuteAsync method to sleep for duration. If duration is null, sleep indefinitely until WakeUp is called.
    protected Task SleepAsync(TimeSpan? duration, CancellationToken stoppingToken)
    {
        var ts = duration ?? TimeSpan.FromMilliseconds(-1);
        return SleepAsync(ts, ts, stoppingToken);
    }
    
    //
    
    // Call me in your ExecuteAsync method to sleep for a random duration between duration1 and duration2
    protected async Task SleepAsync(TimeSpan duration1, TimeSpan duration2, CancellationToken stoppingToken)
    {
        if (duration1 > duration2)
        {
            throw new ArgumentException("duration1 must be less than or equal to duration2");
        }
        
        var duration = Random.Next((int)duration1.TotalMilliseconds, (int)duration2.TotalMilliseconds);
        try
        {
            var wakeUp = _wakeUpEvent.WaitAsync(stoppingToken);
            var delay = Task.Delay(duration, stoppingToken);
            
            // Sleep for duration or until WakeUp is signalled
            await Task.WhenAny(wakeUp, delay);
        }
        catch (OperationCanceledException)
        {
            // ignore - this is expected and will happen when stoppingToken is cancelled
        }
        finally
        {
            _wakeUpEvent.Reset();            
        }
    }

    //

    // Call me from anywhere to wake up the service from SleepAsync
    public virtual void WakeUp()
    {
        _wakeUpEvent.Set();
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

