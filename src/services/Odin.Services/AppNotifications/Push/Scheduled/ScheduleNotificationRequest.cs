#nullable enable

using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push.Scheduled;

/// <summary>
/// Request to schedule a (push) notification to be sent at a later time.
/// </summary>
public class ScheduleNotificationRequest
{
    /// <summary>
    /// The notification to send.  Same shape accepted by the immediate /notify/push endpoint.
    /// </summary>
    public AppNotificationOptions? Options { get; set; }

    /// <summary>
    /// When to send the notification (UTC, milliseconds since epoch).  A time in the past sends ASAP.
    /// </summary>
    public UnixTimeUtc SendAt { get; set; }

    /// <summary>
    /// If set, repeats the notification every <c>RecurrenceInterval</c> milliseconds after each
    /// occurrence's intended send time, starting from <see cref="SendAt"/>.  Must be at least
    /// <see cref="ScheduledNotificationService.MinRecurrenceInterval"/>.  Omit for a one-shot notification.
    /// </summary>
    public long? RecurrenceInterval { get; set; }
}

/// <summary>
/// Result of scheduling a notification.
/// </summary>
public class ScheduleNotificationResult
{
    /// <summary>
    /// Id of the scheduled job; pass this to the cancel endpoint to unschedule.
    /// </summary>
    public System.Guid JobId { get; set; }
}

/// <summary>
/// Summary of a tenant's scheduled notification, as returned by the list endpoint.
/// </summary>
public class ScheduledNotificationSummary
{
    /// <summary>
    /// Id of the scheduled job; pass this to the cancel endpoint.
    /// </summary>
    public System.Guid JobId { get; set; }

    /// <summary>
    /// The notification to send.
    /// </summary>
    public AppNotificationOptions? Options { get; set; }

    /// <summary>
    /// When the notification is scheduled to be sent (UTC, milliseconds since epoch).
    /// </summary>
    public UnixTimeUtc SendAt { get; set; }

    /// <summary>
    /// Current state of the underlying job (e.g. Scheduled, Failed).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// How many times this notification has been attempted so far.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// The maximum number of attempts before the notification is given up on.
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    /// If set, this notification repeats every <c>RecurrenceInterval</c> milliseconds; null means it's
    /// a one-shot notification.
    /// </summary>
    public long? RecurrenceInterval { get; set; }
}
