using System.Threading.Tasks;
using Odin.Services.Background;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Hosting.Cli;

#nullable enable

// The command line runs no background services, so there is nothing to wake: a job it schedules (the payload
// move an import starts) is picked up by a running host's job runner, which polls the jobs table
public sealed class NothingToWakeNotifier<T> : IBackgroundServiceNotifier<T> where T : AbstractBackgroundService
{
    public Task NotifyWorkAvailableAsync(string? serviceIdentifier = null) => Task.CompletedTask;
}
