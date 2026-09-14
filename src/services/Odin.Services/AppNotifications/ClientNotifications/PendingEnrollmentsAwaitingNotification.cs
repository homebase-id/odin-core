using System;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications;

/// <summary>
/// Tells an app that a review left it circle enrollments only it can complete, so it can act on them
/// while it happens to be running.
/// </summary>
/// <remarks>
/// A review runs inside one app's client, but the owner may choose circles belonging to others, and only
/// the app owning a circle can source its drives' storage keys.  Those choices are recorded against the
/// connection and wait; this is how the waiting app finds out, rather than discovering it by chance the
/// next time it looks.
/// <para>
/// A hint, not a handoff.  The app still completes the work by asking -- over the socket with
/// <c>ProcessEnrollments</c>, or through <c>POST connections/enrollments/process</c> -- and an app that
/// never receives this loses nothing but time: the queue is durable, and the owner's upgrade pass sweeps
/// up whatever no app ever comes back for.
/// </para>
/// <para>
/// Targeted, so it reaches only the app that can act on it: an app has no business learning which
/// contacts the owner reviewed into some other app's circles.
/// </para>
/// </remarks>
public class PendingEnrollmentsAwaitingNotification : MediatorNotificationBase, IClientNotification,
    IAppTargetedClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.PendingEnrollmentsAwaiting;

    public Guid NotificationTypeId { get; } = Guid.Parse("6f2c1d84-9a3b-4e77-b0d2-5c8e41a6f913");

    /// <summary>The app that owns the circle, and so the only one that can complete the enrollment.</summary>
    public Guid TargetAppId { get; init; }

    /// <summary>The contact the enrollment is about.</summary>
    public OdinId OdinId { get; init; }

    /// <summary>The circle the owner asked for.</summary>
    public Guid CircleId { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            OdinId = this.OdinId.DomainName,
            CircleId = this.CircleId
        });
    }
}
