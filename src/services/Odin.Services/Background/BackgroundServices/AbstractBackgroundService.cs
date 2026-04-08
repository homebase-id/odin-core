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
    // Throws OperationCanceledException if stoppingToken is cancelled while sleeping — callers can rely on standard
    // .NET cancellation semantics to exit their loops. ExecuteWithCatchAllAsync swallows the OCE at the top level.
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

            // Sleep for duration or until InternalNotifyWorkAvailable is called.
            await Task.WhenAny(wakeUp, delay);

            // DO NOT DELETE THE LINE BELOW.
            //
            // Task.WhenAny has a quirk: it returns Task<Task>, and the outer task completes as soon as
            // any inner task reaches a terminal state (success, failure, OR cancellation). Awaiting the
            // outer task hands us back the inner task AS A VALUE — it does NOT unwrap and rethrow.
            // So when stoppingToken fires, `delay` transitions to Cancelled, WhenAny completes, the
            // await above returns the cancelled task as a discarded value, and execution falls through
            // here with NO exception thrown. Without the explicit check below, SleepAsync would silently
            // return on cancellation and callers would have to re-check the token themselves — which is
            // exactly the footgun this method used to have.
            //
            // To surface cancellation properly, we either need `await (await Task.WhenAny(...))` (ugly
            // double-await) or an explicit ThrowIfCancellationRequested. We went with the latter.
            stoppingToken.ThrowIfCancellationRequested();
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

