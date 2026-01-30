using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Odin.Services.Background.BackgroundServices;

#nullable enable

//

public abstract class AbstractBackgroundService(ILogger logger)
{
    public bool IsStarted { get; private set; }

    private static readonly Random Random = new();
    private readonly AsyncManualResetEvent _wakeUpEvent = new();
    private CancellationTokenSource? _stoppingCts;
    private Task? _task;

    // Maximum sleep duration. Sleeping too long makes Delay behave unpredictably, so cap it at some reasonable number.
    public static readonly TimeSpan MaxSleepDuration = TimeSpan.FromDays(7);

    // Override initialization logic here. BackgroundServiceManager will wait for this to complete before starting the service.
    protected virtual Task StartingAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    // Implement me to do your work
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    //

    // Override cleanup logic here. BackgroundServiceManager will run this after the service has stopped.
    protected virtual Task StoppedAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    //

    // Call me in your ExecuteAsync method to sleep for duration.
    // Call InternalNotifyWorkAvailable() to wake up.
    protected Task SleepAsync(TimeSpan duration, CancellationToken stoppingToken)
    {
        return SleepAsync(duration, duration, stoppingToken);
    }

    //

    // Call me in your ExecuteAsync method to sleep for a random duration between duration1 and duration2 (max 7 days)
    // Call InternalNotifyWorkAvailable() to wake up.
    protected async Task SleepAsync(TimeSpan duration1, TimeSpan duration2, CancellationToken stoppingToken)
    {
        if (duration1 == TimeSpan.Zero && duration2 == TimeSpan.Zero)
        {
            return;
        }
        if (duration1 < TimeSpan.Zero)
        {
            logger.LogError("Invalid duration1 {duration1}ms. Resetting to min.", duration1.TotalMilliseconds);
            duration1 = TimeSpan.Zero;
        }
        if (duration1 > MaxSleepDuration)
        {
            logger.LogError("Invalid duration1 {duration1}ms. Resetting to max.", duration1.TotalMilliseconds);
            duration1 = MaxSleepDuration;
        }
        if (duration2 < TimeSpan.Zero)
        {
            logger.LogError("Invalid duration2 {duration2}ms. Resetting to min.", duration2.TotalMilliseconds);
            duration2 = TimeSpan.Zero;
        }
        if (duration2 > MaxSleepDuration)
        {
            logger.LogError("Invalid duration2 {duration2}ms. Resetting to max.", duration2.TotalMilliseconds);
            duration2 = MaxSleepDuration;
        }

        if (duration1 > duration2)
        {
            throw new ArgumentException("duration1 must be less than or equal to duration2");
        }

        var duration = Random.Next((int)duration1.TotalMilliseconds, (int)duration2.TotalMilliseconds);
        try
        {
            var wakeUp = _wakeUpEvent.WaitAsync(stoppingToken);
            var delay = Task.Delay(duration, stoppingToken);

            // Sleep for duration or until InternalNotifyWorkAvailable is called
            await Task.WhenAny(wakeUp, delay);
        }
        catch (OperationCanceledException)
        {
            // ignore - this is expected and will happen when stoppingToken is canceled
        }
        finally
        {
            _wakeUpEvent.Reset();
        }
    }

    //

    // Call me through BackgroundServiceManager to wake up the service from SleepAsync
    internal void InternalNotifyWorkAvailable()
    {
        _wakeUpEvent.Set();
    }

    //

    internal async Task InternalStartAsync(CancellationToken stoppingToken)
    {
        if (!IsStarted)
        {
            IsStarted = true;

            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await StartingAsync(_stoppingCts.Token);

            // No 'await' here, this is intentional; we want to start the task and return immediately
            _task = ExecuteWithCatchAllAsync(_stoppingCts.Token);
        }
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
            IsStarted = false;
        }
    }

    //

}

