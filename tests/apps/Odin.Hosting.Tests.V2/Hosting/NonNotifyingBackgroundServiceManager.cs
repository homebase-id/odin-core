#nullable enable
using System.Threading.Tasks;
using Odin.Services.Background;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Tenant-scope decorator over the real <see cref="IBackgroundServiceManager"/> that turns the
/// <c>NotifyWorkAvailableAsync</c> calls into no-ops.
/// </summary>
/// <remarks>
/// The V2 framework wires background services into DI but never <c>StartAsync</c>'s them
/// (see <c>SetBackgroundServicesBaseline</c>); tests drain the outbox/inbox explicitly via
/// <see cref="ITestSync"/>. The real manager, however, treats a notify for an unstarted service as
/// a misconfiguration: it spins for 30s waiting for the service to appear and then throws
/// "Background service not found". That surfaces on peer-facing paths — e.g. receiving a connection
/// request publishes <c>ConnectionRequestReceivedNotification</c>, whose PushNotificationService
/// handler enqueues to the PeerOutbox and notifies <c>PeerOutboxProcessorBackgroundService</c> —
/// turning the 30s-then-throw into a 500 and failing every peer test. Every other manager call is
/// delegated to the real instance so start/stop/shutdown still behave normally.
/// </remarks>
public sealed class NonNotifyingBackgroundServiceManager(IBackgroundServiceManager inner)
    : IBackgroundServiceManager
{
    public T Create<T>(string? serviceIdentifier = null) where T : AbstractBackgroundService
        => inner.Create<T>(serviceIdentifier);

    public Task StartAsync(AbstractBackgroundService service) => inner.StartAsync(service);

    public Task<T> StartAsync<T>(string? serviceIdentifier = null) where T : AbstractBackgroundService
        => inner.StartAsync<T>(serviceIdentifier);

    public Task StopAsync(string serviceIdentifier) => inner.StopAsync(serviceIdentifier);

    public Task StopAsync<T>() => inner.StopAsync<T>();

    public Task StopAllAsync() => inner.StopAllAsync();

    public Task ShutdownAsync() => inner.ShutdownAsync();

    // The whole point: ignore "work available" instead of spinning 30s and throwing.
    public Task NotifyWorkAvailableAsync(string serviceIdentifier) => Task.CompletedTask;

    public Task NotifyWorkAvailableAsync<T>() => Task.CompletedTask;
}
