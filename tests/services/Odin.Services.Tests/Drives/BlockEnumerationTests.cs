using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// BlockEnumeration is opt-in to restrict: absent means listable, so every drive stored before the flag existed keeps
/// its behaviour.  A system drive has what its built-in definition says, whatever is stored.
/// </summary>
[TestFixture]
public class BlockEnumerationTests
{
    [Test]
    public void ADriveStoredBeforeTheFlagExistedCanBeListed()
    {
        const string legacyJson = """
            {"Metadata":"","OwnerOnly":false,"IsReadonly":false,
             "AllowAnonymousReads":true,"AllowSubscriptions":false,"AllowCdn":false,"IsArchived":false}
            """;

        var details = OdinSystemSerializer.Deserialize<StorageDriveDetails>(legacyJson);

        Assert.That(details, Is.Not.Null);
        Assert.That(details!.BlockEnumeration, Is.False);
    }

    [TestCase(false, TestName = "ASystemDriveStoredWithoutTheFlagStillHasItsDefinitions")]
    [TestCase(true, TestName = "ASystemDriveStoredWithTheFlagHasItsDefinitions")]
    public void ASystemDriveHasWhatItsDefinitionSays(bool stored)
    {
        // WebDrop's drive, as an identity made before the option existed stores it, blocks enumeration all the same
        var webDrop = new StorageDrive(null!, new StorageDriveData
        {
            TargetDriveInfo = WellKnownAppDrives.WebDropDrive,
            BlockEnumeration = stored
        });
        Assert.That(webDrop.BlockEnumeration, Is.EqualTo(BuiltinDrives.WebDropDrive.BlockEnumeration));
        Assert.That(webDrop.BlockEnumeration, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnyOtherDriveHasWhatIsStored(bool stored)
    {
        var drive = new StorageDrive(null!, new StorageDriveData
        {
            TargetDriveInfo = TargetDrive.NewTargetDrive(),
            BlockEnumeration = stored
        });
        Assert.That(drive.BlockEnumeration, Is.EqualTo(stored));
    }
}
