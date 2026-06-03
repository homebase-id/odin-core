#nullable enable

using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push.Scheduled;

/// <summary>
/// Serializable state for a <see cref="ScheduledNotificationJob"/>.  Captures everything needed to
/// enqueue a (push) notification on behalf of a tenant at a later time.
/// </summary>
public class ScheduledNotificationJobData
{
    /// <summary>
    /// The tenant (identity) that owns the notification and whose subscriptions it will be pushed to.
    /// </summary>
    public OdinId? Tenant { get; init; }

    /// <summary>
    /// The sender recorded on the notification (typically the tenant itself when scheduled by owner/app).
    /// </summary>
    public OdinId? SenderId { get; init; }

    /// <summary>
    /// The notification to send.  Mirrors what the immediate /notify/push endpoint accepts.
    /// </summary>
    public AppNotificationOptions? Options { get; init; }

    /// <summary>
    /// The originally requested send time.  Retained for diagnostics; the actual scheduling is driven
    /// by the job's <c>RunAt</c>.
    /// </summary>
    public UnixTimeUtc SendAt { get; init; }
}
