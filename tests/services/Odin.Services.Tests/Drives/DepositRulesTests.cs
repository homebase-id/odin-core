#nullable enable
using System;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// One set of deposit rules for every drive that takes one (docs/server-share-sealed-arrivals-plan.md, "Decisions"):
/// which drives take a deposit, and that a comment is never one.
/// </summary>
public class DepositRulesTests
{
    private static StorageDrive ServerShare(bool allowSubscriptions = false, bool communityChannel = false, bool withKeyPair = true,
        Guid? id = null) =>
        new(null, new StorageDriveData
        {
            Id = id ?? Guid.NewGuid(),
            SharingModel = DriveSharingModel.Collaborative,
            AllowSubscriptions = allowSubscriptions,
            Attributes = communityChannel ? new() { [BuiltInDriveAttributes.IsCollaborativeChannel] = "true" } : null,
            WriteOnlyKeyPair = withKeyPair ? DriveWriteOnlyKey.FromPublicKeyJwk(PublicKeyJwk) : null
        });

    // Any P-384 public key: the rules ask only that the drive has a write-only key pair
    private const string PublicKeyJwk =
        "{\"kty\":\"EC\",\"crv\":\"P-384\",\"x\":\"yjx33dKI7M4I3IUdOl1Tdqf4mqLGyXnYEBg_Oo3B9_QjOxI1ol2usFU1t3FhnQ18\",\"y\":\"FkGMeDRw4PJSFlCwESYb59Nltf3qE7Q5IXaUK2OiplW8aWP3oTWZQ5YZYdzJ7ReN\"}";

    [Test]
    public void AServerShareDriveTakesADepositUnlessItIsACommunityChannel()
    {
        Assert.That(ServerShare().CanHoldDeposits, Is.True);
        Assert.That(ServerShare(allowSubscriptions: true).CanHoldDeposits, Is.True, "followers get only the owner's posts");
        Assert.That(ServerShare(communityChannel: true).CanHoldDeposits, Is.False, "it sends a member's post on as it lands");
        Assert.That(ServerShare(withKeyPair: false).CanHoldDeposits, Is.False, "nothing a sender could seal to");
        Assert.That(ServerShare(id: WellKnownAppDrives.ShardRecoveryDrive.Alias.Value).CanHoldDeposits, Is.False,
            "shards stay sealed");
    }

    // Not Selective Share: its comments take only a fileKey key header, refused by an earlier rule
    [TestCase(DriveSharingModel.Collaborative)]
    [TestCase(DriveSharingModel.Private)]
    public void ACommentIsNeverSealed(DriveSharingModel model)
    {
        var drive = model == DriveSharingModel.Collaborative ? ServerShare() : TestDrives.Of(model);

        Assert.DoesNotThrow(() => ZkDriveFiles.AssertDriveTakes(drive, EncryptionType.Sealed, FileSystemType.Standard), "a file");
        Assert.Throws<OdinClientException>(() => ZkDriveFiles.AssertDriveTakes(drive, EncryptionType.Sealed, FileSystemType.Comment),
            "a comment");
    }
}
