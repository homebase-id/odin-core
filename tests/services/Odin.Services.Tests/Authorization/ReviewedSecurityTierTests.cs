using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Tests.Authorization;

/// <summary>
/// Review is always required: an unreviewed connection is evaluated as Authenticated, except on an identity
/// whose data predates the upgrade that fills in <c>ReviewedAt</c>, where everyone is still treated as reviewed.
/// </summary>
public class ReviewedSecurityTierTests
{
    private const int Current = ReviewedSecurityTier.ReviewedAtBackfilledVersion;
    private const int BeforeBackfill = ReviewedSecurityTier.ReviewedAtBackfilledVersion - 1;

    [Test]
    public void TheUnreviewedAreDemoted()
    {
        Assert.That(ReviewedSecurityTier.For(Current, (UnixTimeUtc?)null), Is.EqualTo(SecurityGroupType.Authenticated));
    }

    [Test]
    public void TheReviewedStayConnected()
    {
        Assert.That(ReviewedSecurityTier.For(Current, UnixTimeUtc.Now()), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void BeforeTheBackfill_NobodyIsDemoted()
    {
        // No connection carries a review date yet, so demoting the unreviewed would demote the lot.
        Assert.That(ReviewedSecurityTier.For(BeforeBackfill, (UnixTimeUtc?)null), Is.EqualTo(SecurityGroupType.Connected));
        Assert.That(ReviewedSecurityTier.For(0, (UnixTimeUtc?)null), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void IcrOverloadReadsReviewedAt()
    {
        var unreviewed = new IdentityConnectionRegistration { ReviewedAt = null };
        var reviewed = new IdentityConnectionRegistration { ReviewedAt = UnixTimeUtc.Now() };

        Assert.That(ReviewedSecurityTier.For(Current, unreviewed), Is.EqualTo(SecurityGroupType.Authenticated));
        Assert.That(ReviewedSecurityTier.For(Current, reviewed), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void NullIcrIsTreatedAsUnreviewed()
    {
        // Reaching the tier decision with no registration at all should not hand out Connected.
        Assert.That(ReviewedSecurityTier.For(Current, (IdentityConnectionRegistration?)null),
            Is.EqualTo(SecurityGroupType.Authenticated));
    }

    [Test]
    public void EffectiveLevel_DemotesAnUnreviewedConnection()
    {
        Assert.That(ReviewedSecurityTier.EffectiveLevel(TenantAt(Current), Connection(reviewed: false)),
            Is.EqualTo(SecurityGroupType.Authenticated));
        Assert.That(ReviewedSecurityTier.EffectiveLevel(TenantAt(Current), Connection(reviewed: true)),
            Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void EffectiveLevel_DemotesNobodyBeforeTheBackfill()
    {
        Assert.That(ReviewedSecurityTier.EffectiveLevel(TenantAt(BeforeBackfill), Connection(reviewed: false)),
            Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void EffectiveLevel_LeavesCallersWhoAreNotConnectionsAlone()
    {
        var stranger = new CallerContext(new OdinId("stranger.dotyou.cloud"), null, SecurityGroupType.Authenticated);
        Assert.That(ReviewedSecurityTier.EffectiveLevel(TenantAt(Current), stranger),
            Is.EqualTo(SecurityGroupType.Authenticated));
    }

    private static TenantContext TenantAt(int dataVersion)
    {
        var tenantContext = new TenantContext();
        tenantContext.UpdateDataVersion(dataVersion);
        return tenantContext;
    }

    private static CallerContext Connection(bool reviewed) =>
        new(new OdinId("sam.dotyou.cloud"), null, SecurityGroupType.Connected) { IsReviewed = reviewed };
}
