using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Contacts;

/// <summary>
/// Keeps contact files in step with the connection/introduction lifecycle. Subscribes to the in-process
/// MediatR notifications and, on the connection hot path, ensures a contact file exists for each
/// identity a relationship appears with — and, once connected, pulls the now-available peer profile.
///
/// <para>
/// Handlers are <b>fast, local, and fully guarded</b>: ensuring a stub is a single local write; a
/// failure here must never break the connection flow that published the event (everything is wrapped).
/// They only run when the publishing context carries the owner master key (writes need the contact
/// drive storage key) — server-internal/peer contexts are skipped and converge later via owner-driven
/// <c>/sync</c>.
/// </para>
///
/// <para>
/// <b>Enrichment on connect runs inline</b> under the owner's keyed context (mirroring what odin-js does
/// client-side today). A fully <i>detached</i> background enrichment job is not used here: it would need
/// the owner master key (for the storage + ICR keys), which is only reachable from a live owner session
/// token — not from a notification handler. Owner-triggered enrichment (a captured-token job) is the
/// path for moving this off the hot path later.
/// </para>
/// </summary>
public class ContactLifecycleService(
    ILogger<ContactLifecycleService> logger,
    ContactService contactService,
    ContactEnrichmentService contactEnrichmentService) :
    INotificationHandler<ConnectionFinalizedNotification>,
    INotificationHandler<ConnectionRequestReceivedNotification>,
    INotificationHandler<IntroductionsReceivedNotification>
{
    public async Task Handle(ConnectionFinalizedNotification notification, CancellationToken cancellationToken)
    {
        // Now connected: ensure the contact exists and pull the peer profile.
        await SafeRun(notification.OdinId, notification.OdinContext, enrich: true, nameof(ConnectionFinalizedNotification));
    }

    public async Task Handle(ConnectionRequestReceivedNotification notification, CancellationToken cancellationToken)
    {
        // Inbound request: materialize a stub for the sender. Enrichment waits for /sync (the sender
        // isn't connected yet, and this often runs under a non-owner context anyway).
        await SafeRun(notification.Sender, notification.OdinContext, enrich: false, nameof(ConnectionRequestReceivedNotification));
    }

    public async Task Handle(IntroductionsReceivedNotification notification, CancellationToken cancellationToken)
    {
        foreach (var identity in notification.Introduction?.Identities ?? [])
        {
            if (OdinId.IsValid(identity))
            {
                await SafeRun((OdinId)identity, notification.OdinContext, enrich: false, nameof(IntroductionsReceivedNotification));
            }
        }
    }

    private async Task SafeRun(OdinId odinId, IOdinContext odinContext, bool enrich, string trigger)
    {
        // Writes need the contact drive storage key, which derives from the owner master key. Skip
        // contexts that don't have it (server-internal/peer); owner-driven /sync converges them later.
        if (odinContext?.Caller is not { HasMasterKey: true })
        {
            logger.LogDebug("Contact lifecycle ({trigger}) for {odinId}: no master key in context; skipping", trigger, odinId);
            return;
        }

        try
        {
            await contactService.EnsureExistsAsync(odinId, odinContext);

            if (enrich)
            {
                // Best-effort; EnrichAsync swallows peer/profile failures and leaves the contact as-is.
                await contactEnrichmentService.EnrichAsync(odinId, odinContext);
            }
        }
        catch (System.Exception e)
        {
            // Never let a contact-side failure break the connection flow that published this event.
            logger.LogWarning(e, "Contact lifecycle ({trigger}) for {odinId} failed; ignoring", trigger, odinId);
        }
    }
}
