using System;
using Odin.Core.Identity;

namespace Odin.Services.Security.Email;

public enum RecoveryNotificationKind
{
    VerifyRecoveryEmail,
    EnterRecoveryMode,
    ExitRecoveryMode,
    FinalizeRecovery,

    /// <summary>Enough friends approved; the link-free "go back to the browser where you started" email.</summary>
    FriendsApproved
}

/// <summary>
/// A recovery link <see cref="RecoveryNotifier"/> generated for <paramref name="Tenant"/>, with the
/// secrets it carries. <paramref name="FinalRecoveryKey"/> is set only for
/// <see cref="RecoveryNotificationKind.FinalizeRecovery"/>.
/// </summary>
public sealed record RecoveryNotification(
    OdinId Tenant,
    RecoveryNotificationKind Kind,
    string Link,
    Guid NonceId,
    Guid? FinalRecoveryKey = null);

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
