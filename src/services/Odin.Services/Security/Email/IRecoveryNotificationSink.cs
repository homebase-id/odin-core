using System;
using Odin.Core.Identity;

namespace Odin.Services.Security.Email;

public enum RecoveryNotificationKind
{
    VerifyRecoveryEmail,
    EnterRecoveryMode,
    ExitRecoveryMode,

    /// <summary>Enough shards are in; the link-free "go back to the browser where you started" email.</summary>
    ShardsCollected,

    /// <summary>
    /// An automatic holder's own email to the recovering owner: its link releases the holder's shard.
    /// Sent by the holder's host, for the dealer's recovery.
    /// </summary>
    ReleaseShard
}

/// <summary>
/// A recovery link a <see cref="RecoveryNotifier"/> generated for <paramref name="Tenant"/>'s recovery,
/// with the secrets it carries.  <paramref name="Holder"/> is set only for
/// <see cref="RecoveryNotificationKind.ReleaseShard"/>: the holder that sent it.
/// </summary>
public sealed record RecoveryNotification(
    OdinId Tenant,
    RecoveryNotificationKind Kind,
    string Link,
    Guid NonceId,
    OdinId? Holder = null);

/// <summary>
/// Receives every recovery link <see cref="RecoveryNotifier"/> generates, whether or not an email is
/// sent. Production registers <see cref="NullRecoveryNotificationSink"/>; integration tests register
/// a capturing sink so they can follow the links without email, in Debug and Release alike (#1866).
/// Registered at the root, so one host has one sink shared by all its tenants.
/// </summary>
public interface IRecoveryNotificationSink
{
    void Notify(RecoveryNotification notification);
}

/// <summary>
/// The shipped sink: discards everything, so no nonce or key leaves <see cref="RecoveryNotifier"/>
/// except by email.
/// </summary>
public sealed class NullRecoveryNotificationSink : IRecoveryNotificationSink
{
    public void Notify(RecoveryNotification notification)
    {
    }
}
