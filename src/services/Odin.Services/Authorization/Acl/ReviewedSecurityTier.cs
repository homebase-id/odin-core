#nullable enable
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl;

/// <summary>
/// Decides the security tier a connection is treated at when content is evaluated.
/// </summary>
/// <remarks>
/// A connected caller is still admitted at <see cref="SecurityGroupType.Connected"/>, with
/// <see cref="CallerContext.IsReviewed"/> carrying the review stamp, so everything that asks "is this a
/// connection" keeps working.  Content evaluation -- the drive query's security range and the connected-ACL
/// check -- asks this class instead.
/// <para>
/// The recut it implements: a connection the owner has reviewed is <see cref="SecurityGroupType.Connected"/>,
/// one they have not is <see cref="SecurityGroupType.Authenticated"/> -- no better placed than any
/// logged-in stranger, which is what an unreviewed connection is. Off by default, and off means Connected,
/// exactly as before.
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

    /// <summary>
    /// The tier to evaluate content with for this caller: their admitted tier, except that an unreviewed
    /// connection is treated as <see cref="SecurityGroupType.Authenticated"/> when <b>both</b> identities have the
    /// setting on -- this one, and the caller's (announced through <see cref="CallerContext.CallerUsesReviewedTier"/>).
    /// </summary>
    /// <remarks>
    /// Both, not just this one, so a dark launch among a few identities never changes anything for anyone else.
    /// A caller that cannot announce it -- a browser login, an older server -- is never demoted.
    /// </remarks>
    public static SecurityGroupType EffectiveLevel(TenantSettings? settings, CallerContext caller)
    {
        // Only connections are ever demoted
        if (caller.SecurityLevel != SecurityGroupType.Connected)
        {
            return caller.SecurityLevel;
        }

        // A reviewed connection keeps its tier
        if (caller.IsReviewed)
        {
            return SecurityGroupType.Connected;
        }

        // This identity has the tier off
        if (!(settings?.UseReviewedSecurityTier ?? false))
        {
            return SecurityGroupType.Connected;
        }

        // The caller did not announce it has the tier on
        if (!caller.CallerUsesReviewedTier)
        {
            return SecurityGroupType.Connected;
        }

        return SecurityGroupType.Authenticated;
    }
}
