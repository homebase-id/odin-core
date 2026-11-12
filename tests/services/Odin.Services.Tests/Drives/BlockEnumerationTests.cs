using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// BlockEnumeration is opt-in to restrict: absent means listable, so every drive stored before the flag existed keeps
/// its behaviour, and only WebDrop's built-in drive sets it.
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

    [Test]
    public void WebDropsBuiltinDriveBlocksEnumeration()
    {
        Assert.That(BuiltinDrives.WebDropDrive.BlockEnumeration, Is.True);
    }
}
