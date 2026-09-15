using System;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Time;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// Operational state of an identity (shared by every node), ordered from least to most restricted.
/// Persisted in the Registrations json column; the legacy disabled column mirrors <see cref="Disabled"/>.
/// </summary>
public enum TenantStatus
{
    /// <summary>
    /// Normal operation.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Over its storage quota. Everything that does not add payload bytes keeps working.
    /// </summary>
    OutOfQuota = 1,

    /// <summary>
    /// Temporary maintenance hold (e.g. while the identity is being moved). Callers are told to
    /// retry later and the identity's background services are stopped, so its data does not change.
    /// </summary>
    Paused = 2,

    /// <summary>
    /// Administrative end state: this copy of the identity does not serve and its background
    /// services are stopped. See <see cref="DisabledReason"/>.
    /// </summary>
    Disabled = 3
}

public enum DisabledReason
{
    /// <summary>
    /// Disabled by an administrator.
    /// </summary>
    Admin = 0,

    /// <summary>
    /// Disabled because the identity is being deleted.
    /// </summary>
    PendingDeletion = 1,

    /// <summary>
    /// The identity now lives on another host. This copy must never serve again.
    /// </summary>
    Moved = 2
}

public sealed record TenantStatusState(TenantStatus Status, DisabledReason? DisabledReason, UnixTimeUtc? StatusChangedAt);

public static class TenantStatusRules
{
    /// <summary>
    /// Retry-After sent to callers of a paused identity.
    /// </summary>
    public const int PausedRetryAfterSeconds = 600;

    /// <summary>
    /// Parses a status name case-insensitively, ignoring '-' and '_' (so "out-of-quota" works). Numbers and
    /// Enum.TryParse's comma-separated flag syntax are refused.
    /// </summary>
    public static bool TryParse<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        var normalized = value?.Replace("-", "").Replace("_", "").Trim();
        if (string.IsNullOrEmpty(normalized) || !normalized.All(char.IsLetter))
        {
            return false;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out result) && Enum.IsDefined(result);
    }

    public static bool RunsBackgroundServices(TenantStatus status)
    {
        return status switch
        {
            TenantStatus.Active => true,
            TenantStatus.OutOfQuota => true,
            TenantStatus.Paused => false,
            TenantStatus.Disabled => false,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Decide whether this status runs background services")
        };
    }

    /// <summary>
    /// A disabled status always carries a reason; any other status carries none.
    /// </summary>
    public static DisabledReason? NormalizeReason(TenantStatus status, DisabledReason? reason)
    {
        return status == TenantStatus.Disabled ? reason ?? DisabledReason.Admin : reason;
    }

    /// <summary>
    /// Throws <see cref="OdinClientException"/> if the transition is not allowed.
    /// Expects <paramref name="toReason"/> to be normalized with <see cref="NormalizeReason"/>.
    /// </summary>
    public static void Validate(TenantStatus fromStatus, DisabledReason? fromReason, TenantStatus toStatus, DisabledReason? toReason)
    {
        if (!Enum.IsDefined(toStatus))
        {
            throw new OdinClientException($"Unknown tenant status '{toStatus}'");
        }

        if (toReason.HasValue && !Enum.IsDefined(toReason.Value))
        {
            throw new OdinClientException($"Unknown disabled reason '{toReason}'");
        }

        if (toStatus != TenantStatus.Disabled && toReason.HasValue)
        {
            throw new OdinClientException("A disabled reason is only valid for the disabled status");
        }

        if (toStatus == TenantStatus.Disabled && !toReason.HasValue)
        {
            throw new OdinClientException("The disabled status requires a reason");
        }

        // A moved identity lives elsewhere; serving this copy again would split it in two.
        // Deleting the leftover copy is still allowed.
        var isMoved = fromStatus == TenantStatus.Disabled && fromReason == DisabledReason.Moved;
        var staysDisabled = toStatus == TenantStatus.Disabled &&
                            toReason is DisabledReason.Moved or DisabledReason.PendingDeletion;
        if (isMoved && !staysDisabled)
        {
            throw new OdinClientException("This identity has moved to another host and cannot be re-enabled here");
        }

        // Leaving disabled is a deliberate re-enable, straight to active. Otherwise pause-then-resume
        // would re-enable a disabled identity without anyone saying so.
        if (fromStatus == TenantStatus.Disabled && toStatus is TenantStatus.OutOfQuota or TenantStatus.Paused)
        {
            throw new OdinClientException($"A disabled identity can only be enabled (set active), not set to {toStatus}");
        }
    }
}
