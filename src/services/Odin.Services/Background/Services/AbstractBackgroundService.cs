using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Odin.Services.Background.Services;

#nullable enable

public interface IAbstractBackgroundService
{
    void PulseBackgroundProcessor();
}

//

public abstract class AbstractBackgroundService(ILogger logger) : IAbstractBackgroundService
{
    private static readonly Random Random = new();
    private readonly AsyncManualResetEvent _wakeUpEvent = new();
    private CancellationTokenSource? _stoppingCts;
    private Task? _task;

    // Override initialization logic here. BackgroundServiceManager will wait for this to complete before starting the service.
    public virtual Task StartingAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    // Implement me to do your work
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    //

    // Override cleanup logic here. BackgroundServiceManager will run this after the service has stopped.
    public virtual Task StoppedAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    //

    // Call me in your ExecuteAsync method to sleep for duration. If duration is null, sleep indefinitely.
    // Call PulseBackgroundProcessor() to wake up.
    protected Task SleepAsync(TimeSpan? duration, CancellationToken stoppingToken)
    {
        var ts = duration ?? TimeSpan.FromMilliseconds(-1);
        return SleepAsync(ts, ts, stoppingToken);
    }
    
    //
    
    // Call me in your ExecuteAsync method to sleep for a random duration between duration1 and duration2 (max 48 hours)
    // Call PulseBackgroundProcessor() to wake up.
    protected async Task SleepAsync(TimeSpan duration1, TimeSpan duration2, CancellationToken stoppingToken)
    {
        if (duration1 > duration2)
        {
            throw new ArgumentException("duration1 must be less than or equal to duration2");
        }
        
        // Don't sleep for more than 48 hours. Something goes bunkers if it becomes too long a nap.
        if (duration1 > TimeSpan.FromHours(48)) 
        {
            duration1 = TimeSpan.FromHours(48);
        }
        if (duration2 > TimeSpan.FromHours(48)) 
        {
            duration2 = TimeSpan.FromHours(48);
        }
        
        var duration = Random.Next((int)duration1.TotalMilliseconds, (int)duration2.TotalMilliseconds);
        try
        {
            var wakeUp = _wakeUpEvent.WaitAsync(stoppingToken);
            var delay = Task.Delay(duration, stoppingToken);
            
            // Sleep for duration or until PulseBackgroundProcessor is signalled
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
    public void PulseBackgroundProcessor()
    {
        _wakeUpEvent.Set();
    }

    //

    internal async Task InternalStartAsync(CancellationToken stoppingToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await StartingAsync(_stoppingCts.Token);

        // No 'await' here, this is intentional; we want to start the task and return immediately
        _task = ExecuteWithCatchAllAsync(_stoppingCts.Token);
    }

    //

    private async Task ExecuteWithCatchAllAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BackgroundService {type} is exiting because of an unhandled exception: {error}",
                GetType().Name, ex.Message);
        }
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

