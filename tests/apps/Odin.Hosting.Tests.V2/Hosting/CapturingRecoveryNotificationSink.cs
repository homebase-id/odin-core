#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Identity;
using Odin.Services.Security.Email;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Records every recovery link a host's <see cref="RecoveryNotifier"/> generates, so tests can follow
/// them without email. Register one per fixture through <c>ConfigureRootContainer</c>: being a
/// per-host instance, it cannot pick up a neighbouring fixture's links the way the shared log could.
/// </summary>
public sealed class CapturingRecoveryNotificationSink : IRecoveryNotificationSink
{
    private readonly List<RecoveryNotification> _notifications = [];

    public void Notify(RecoveryNotification notification)
    {
        lock (_notifications)
        {
            _notifications.Add(notification);
        }
    }

    /// <summary>How many <paramref name="kind"/> notifications <paramref name="tenant"/> got so far.</summary>
    public int Count(OdinId tenant, RecoveryNotificationKind kind)
    {
        lock (_notifications)
        {
            return _notifications.Count(n => n.Tenant == tenant && n.Kind == kind);
        }
    }

    /// <summary>
    /// The newest <paramref name="kind"/> notification for <paramref name="tenant"/>. Newest because a
    /// test may enter recovery mode twice and must follow the second link, the first nonce being
    /// spent. Throws when there is none, so a missing link reads as a broken arrange.
    /// </summary>
    public RecoveryNotification Latest(OdinId tenant, RecoveryNotificationKind kind)
    {
        lock (_notifications)
        {
            return _notifications.LastOrDefault(n => n.Tenant == tenant && n.Kind == kind)
                   ?? throw new InvalidOperationException($"No {kind} recovery notification was captured for {tenant}");
        }
    }
}
