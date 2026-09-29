using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Tests.Authorization;

/// <summary>
/// The dark-launch switch is only worth having if "off" is provably indistinguishable from the
/// behaviour that shipped, so the off cases matter more here than the on ones.
/// </summary>
public class ReviewedSecurityTierTests
{
    [Test]
    public void OffMeansConnected_EvenWhenNeverReviewed()
    {
        var settings = new TenantSettings { UseReviewedSecurityTier = false };
        Assert.That(ReviewedSecurityTier.For(settings, (UnixTimeUtc?)null), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void OffMeansConnected_WhenReviewed()
    {
        var settings = new TenantSettings { UseReviewedSecurityTier = false };
        Assert.That(ReviewedSecurityTier.For(settings, UnixTimeUtc.Now()), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void DefaultSettingsAreOff()
    {
        Assert.That(TenantSettings.Default.UseReviewedSecurityTier, Is.False);
        Assert.That(new TenantSettings().UseReviewedSecurityTier, Is.False);
        Assert.That(ReviewedSecurityTier.For(new TenantSettings(), (UnixTimeUtc?)null),
            Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void NullSettingsFallsBackToConnected()
    {
        // A tenant that has never written a settings blob must not be demoted by the absence.
        Assert.That(ReviewedSecurityTier.For(null, (UnixTimeUtc?)null), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void OnDemotesTheUnreviewed()
    {
        var settings = new TenantSettings { UseReviewedSecurityTier = true };
        Assert.That(ReviewedSecurityTier.For(settings, (UnixTimeUtc?)null), Is.EqualTo(SecurityGroupType.Authenticated));
    }

    [Test]
    public void OnKeepsTheReviewedConnected()
    {
        var settings = new TenantSettings { UseReviewedSecurityTier = true };
        Assert.That(ReviewedSecurityTier.For(settings, UnixTimeUtc.Now()), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void IcrOverloadReadsReviewedAt()
    {
        var settings = new TenantSettings { UseReviewedSecurityTier = true };

        var unreviewed = new IdentityConnectionRegistration { ReviewedAt = null };
        var reviewed = new IdentityConnectionRegistration { ReviewedAt = UnixTimeUtc.Now() };

        Assert.That(ReviewedSecurityTier.For(settings, unreviewed), Is.EqualTo(SecurityGroupType.Authenticated));
        Assert.That(ReviewedSecurityTier.For(settings, reviewed), Is.EqualTo(SecurityGroupType.Connected));
    }

    [Test]
    public void NullIcrIsTreatedAsUnreviewed()
    {
        // Reaching the tier decision with no registration at all should not hand out Connected.
        var settings = new TenantSettings { UseReviewedSecurityTier = true };
        Assert.That(ReviewedSecurityTier.For(settings, (IdentityConnectionRegistration?)null),
            Is.EqualTo(SecurityGroupType.Authenticated));
    }
}
