using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Identity;

WIP WIP WIP

namespace Odin.Services.Tenant;

// public interface ITenantBackgroundService
// {
//     Task StartAsync(Func<Task> actions, CancellationToken stopToken);
//     Task StopAsync(CancellationToken cancellationToken);
// }

public sealed class TenantBackgroundServiceManager(ILogger<TenantBackgroundServiceManager> logger) : IDisposable
{
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly object _mutex = new();
    private readonly Dictionary<string, TenantBackgroundService> _backgroundServices = new ();
    private Task _taskRunner;

    //

    public void Start(string serviceIdentifier, Func<CancellationToken, Task> backgroundService)
    {
        lock (_mutex)
        {
            if (_backgroundServices.ContainsKey(serviceIdentifier))
            {
                throw new InvalidOperationException($"The background '{serviceIdentifier}' is already running.");
            }

            var serviceStoppingToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);

            var taskRunner = Task.Run(async () =>
            {
                try
                {
                    await backgroundService(serviceStoppingToken.Token);
                }
                catch (OperationCanceledException) when (_stoppingCts.IsCancellationRequested)
                {
                    // Expected cancellation, ignore
                }
                catch (Exception ex)
                {
                    // Log or handle the exception as needed.
                    // For example:
                    Console.WriteLine($"Unhandled exception in background task: {ex}");
                }
            }, _stoppingCts.Token);

            _backgroundServices.Add(serviceIdentifier, new TenantBackgroundService(taskRunner, serviceStoppingToken));
        }
    }

    //

    public async Task StopAsync()
    {
        await _stoppingCts.CancelAsync();
        await _taskRunner;
    }

    //

    public void Dispose()
    {
        _stoppingCts.Dispose();
    }


}

//

public class TenantBackgroundService : IDisposable
{
    public CancellationTokenSource ServiceStoppingCts { get; private set; }
    public Task TaskRunner { get; private set; }

    //

    public TenantBackgroundService(Task taskRunner, CancellationTokenSource serviceStoppingCts)
    {
        TaskRunner = taskRunner;
        ServiceStoppingCts = serviceStoppingCts;
    }

    public void Dispose()
    {
        ServiceStoppingCts.Dispose();
    }

}

