#nullable enable
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl;

/// <summary>
/// Decides the security tier a connected caller is admitted at.
/// </summary>
/// <remarks>
/// One place, called from all three sites that build a connected caller, so the rule cannot drift
/// between the transit, YouAuth and home paths -- they sit in three different services and a copy of
/// the ternary in each is exactly how they would diverge.
/// <para>
/// The recut it implements: a connection the owner has reviewed is <see cref="SecurityGroupType.Connected"/>,
/// one they have not is <see cref="SecurityGroupType.Authenticated"/> -- no better placed than any
/// logged-in stranger, which is what an unreviewed connection is. Off by default, and off means the
/// answer every one of those sites gave before this existed.
/// </para>
/// </remarks>
public static class ReviewedSecurityTier
{
    /// <param name="reviewedAt">
    /// When the owner reviewed this connection, or null if they never have.  Null with the setting on is
    /// what demotes; the setting is refused on a tenant that has not run the upgrade which fills this in,
    /// so null here means genuinely unreviewed rather than not yet backfilled.
    /// </param>
    public static SecurityGroupType For(TenantSettings? settings, UnixTimeUtc? reviewedAt)
    {
        if (!(settings?.UseReviewedSecurityTier ?? false))
        {
            return SecurityGroupType.Connected;
        }

        return reviewedAt.HasValue ? SecurityGroupType.Connected : SecurityGroupType.Authenticated;
    }

    /// <summary>Convenience for callers that hold the registration rather than the timestamp.</summary>
    public static SecurityGroupType For(TenantSettings? settings, IdentityConnectionRegistration? icr)
        => For(settings, icr?.ReviewedAt);
}
