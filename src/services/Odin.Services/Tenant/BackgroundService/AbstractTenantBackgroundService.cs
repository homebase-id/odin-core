using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Tenant.BackgroundService;

#nullable enable

public abstract class AbstractTenantBackgroundService
{
    private CancellationTokenSource? _stoppingCts;
    private Task? _task;

    //

    // Put any initialization logic here. ServiceManager will wait for this to complete before starting the service.
    public virtual Task StartingAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    // Implement me to do your work
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    //

    // Put any cleanup logic here. ServiceManager will run this after the service has stopped.
    public virtual Task StoppedAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
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

