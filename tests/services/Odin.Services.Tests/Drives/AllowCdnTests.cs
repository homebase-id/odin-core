using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// AllowCdn is opt-in and has no legacy fallback: it replaced a "blockcdn" string in the drive's
/// free-form attribute bag outright, so a drive stored before the flag existed simply reads as
/// false and is not CDN-readable until its owner says so.
/// </summary>
[TestFixture]
public class AllowCdnTests
{
    [Test]
    public void DriveStoredBeforeTheFlagExistedIsNotCdnEnabled()
    {
        // detailsJson as written before AllowCdn existed - note it is anonymous-readable and has
        // no blockcdn attribute, which under the retired rule made it CDN-eligible. It must now
        // come back false: the attribute is gone and nothing infers eligibility any more.
        const string legacyJson = """
            {"Metadata":"","OwnerOnly":false,"IsReadonly":false,
             "AllowAnonymousReads":true,"AllowSubscriptions":false,"IsArchived":false}
            """;

        var details = OdinSystemSerializer.Deserialize<StorageDriveDetails>(legacyJson);

        Assert.That(details, Is.Not.Null);
        Assert.That(details!.AllowCdn, Is.False);
    }

    [Test]
    public void ALingeringBlockCdnAttributeIsInert()
    {
        // Existing drives may still carry the retired attribute. Nothing reads it, so it must not
        // influence anything either way.
        const string legacyJson = """
            {"Metadata":"","OwnerOnly":false,"IsReadonly":false,
             "AllowAnonymousReads":false,"AllowSubscriptions":false,"IsArchived":false,
             "Attributes":{"blockcdn":"false"}}
            """;

        var details = OdinSystemSerializer.Deserialize<StorageDriveDetails>(legacyJson);

        Assert.That(details, Is.Not.Null);
        Assert.That(details!.AllowCdn, Is.False);
    }

    [Test]
    public void AllowCdnRoundTripsThroughSerialization()
    {
        var details = new StorageDriveDetails { AllowCdn = true };
        var restored = OdinSystemSerializer.Deserialize<StorageDriveDetails>(
            OdinSystemSerializer.Serialize(details));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.AllowCdn, Is.True);
    }

    //
    // IsCdnEnabled / SetCdnEnabled - the accessor pair every call site goes through, so the rule
    // can be changed in one place. TenantPathManager is not touched by either.
    //

    private static StorageDrive DriveWith(bool allowCdn) =>
        new(null, new StorageDriveData { AllowCdn = allowCdn });

    [Test]
    public void IsCdnEnabledReportsTheFlag()
    {
        Assert.That(DriveWith(true).IsCdnEnabled(), Is.True);
        Assert.That(DriveWith(false).IsCdnEnabled(), Is.False);
    }

    [Test]
    public void SetCdnEnabledRoundTripsThroughIsCdnEnabled()
    {
        var drive = DriveWith(false);

        drive.SetCdnEnabled(true);
        Assert.That(drive.IsCdnEnabled(), Is.True);

        drive.SetCdnEnabled(false);
        Assert.That(drive.IsCdnEnabled(), Is.False);
    }

    [Test]
    public void CdnEligibilityIsIndependentOfAnonymousReadsAndOwnerOnly()
    {
        // There is no owner-only guard by decision, so an owner-only drive can be CDN-enabled if
        // the owner explicitly asks for it. It is never enabled implicitly.
        var ownerOnly = new StorageDrive(null, new StorageDriveData
        {
            AllowCdn = true,
            AllowAnonymousReads = false,
            OwnerOnly = true
        });

        Assert.That(ownerOnly.IsCdnEnabled(), Is.True);
        Assert.That(ownerOnly.AllowAnonymousReads, Is.False);
        Assert.That(ownerOnly.OwnerOnly, Is.True);
    }

    //
    // System drive seeding
    //

    [Test]
    public void OnlyThePublicPostsSystemDriveIsSeededCdnEnabled()
    {
        // Public Posts is seeded on purpose: it is the content the CDN exists to serve, and one
        // enabled drive is what lets the CDN authenticate at all (CdnAuthPathHandler fails when
        // the enabled set is empty, which would take the health ping down too).
        Assert.That(SystemDriveConstants.CreatePublicPostsChannelDriveRequest.AllowCdn, Is.True);

        // Everything else stays off - the seed must not quietly widen.
        Assert.Multiple(() =>
        {
            Assert.That(SystemDriveConstants.CreateProfileDriveRequest.AllowCdn, Is.False);
            Assert.That(SystemDriveConstants.CreateHomePageConfigDriveRequest.AllowCdn, Is.False);
            Assert.That(SystemDriveConstants.CreateFeedDriveRequest.AllowCdn, Is.False);
            Assert.That(SystemDriveConstants.CreateContactDriveRequest.AllowCdn, Is.False);
            Assert.That(SystemDriveConstants.CreateWalletDriveRequest.AllowCdn, Is.False);
            Assert.That(SystemDriveConstants.CreateChatDriveRequest.AllowCdn, Is.False);
            Assert.That(SystemDriveConstants.CreateMailDriveRequest.AllowCdn, Is.False);
        });
    }

    [Test]
    public void ANewDriveRequestDefaultsToCdnDisabled()
    {
        Assert.That(new CreateDriveRequest().AllowCdn, Is.False);
    }
}
