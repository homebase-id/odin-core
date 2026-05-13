#nullable enable
using System.Threading.Tasks;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.Background.Testing;

/// <summary>
/// Drop-in replacement for <see cref="BackgroundServiceNotifier{T}"/> used by the V2 in-process
/// test framework. Tests don't <c>StartAsync</c> background services, so the production notifier's
/// 30-second retry-until-found behavior would hang every peer call that enqueues outbox work.
/// In test mode the drain is driven explicitly via <see cref="ITestSync"/>, so the wake-up signal
/// is unnecessary — this notifier just no-ops.
/// </summary>
public sealed class NoopBackgroundServiceNotifier<T> : IBackgroundServiceNotifier<T>
    where T : AbstractBackgroundService
{
    public Task NotifyWorkAvailableAsync(string? serviceIdentifier = null) => Task.CompletedTask;
}
