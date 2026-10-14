#nullable enable
using Odin.Core.Time;
using Odin.Services.Base;
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
/// A connection the owner has reviewed is <see cref="SecurityGroupType.Connected"/>; one they have not is
/// <see cref="SecurityGroupType.Authenticated"/> -- no better placed than any logged-in stranger, which is what
/// an unreviewed connection is.  Review is what the retired Confirmed Connections circle used to stand for.
/// </para>
/// <para>
/// The one exception is an identity whose data predates <see cref="ReviewedAtBackfilledVersion"/>: until that
/// upgrade runs, no connection carries a review date, so every one of them is treated as reviewed rather than
/// demoting the whole address book at once.  Upgrades run when the owner next signs in.
/// </para>
/// </remarks>
public static class ReviewedSecurityTier
{
    /// <summary>
    /// The data version from which <c>ReviewedAt</c> is filled in (v15 -&gt; v16 backfills it from prior
    /// Confirmed-circle membership).
    /// </summary>
    public const int ReviewedAtBackfilledVersion = 16;

    /// <param name="dataVersionNumber">This identity's data version.</param>
    /// <param name="reviewedAt">When the owner reviewed this connection, or null if they never have.</param>
    public static SecurityGroupType For(int dataVersionNumber, UnixTimeUtc? reviewedAt)
    {
        if (dataVersionNumber < ReviewedAtBackfilledVersion)
        {
            return SecurityGroupType.Connected;
        }

        return reviewedAt.HasValue ? SecurityGroupType.Connected : SecurityGroupType.Authenticated;
    }

    /// <summary>Convenience for callers that hold the registration rather than the timestamp.</summary>
    public static SecurityGroupType For(int dataVersionNumber, IdentityConnectionRegistration? icr)
        => For(dataVersionNumber, icr?.ReviewedAt);

    /// <summary>
    /// The tier to evaluate content with for this caller: their admitted tier, except that an unreviewed
    /// connection is treated as <see cref="SecurityGroupType.Authenticated"/>.
    /// </summary>
    public static SecurityGroupType EffectiveLevel(TenantContext tenantContext, CallerContext caller)
    {
        // Only connections are ever demoted
        if (caller.SecurityLevel != SecurityGroupType.Connected)
        {
            return caller.SecurityLevel;
        }

        return caller.IsReviewed
            ? SecurityGroupType.Connected
            : For(tenantContext.DataVersionNumber, reviewedAt: null);
    }
}
