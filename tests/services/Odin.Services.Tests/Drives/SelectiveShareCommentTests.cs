#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// A member's comments and reactions on a Selective Share drive (docs/selective-share-pr3-plan.md, section 10): the
/// key header a comment takes (fileKey), and the share rows that let a caller in (CallerShares).
/// </summary>
public class SelectiveShareCommentTests
{
    private static readonly StorageDrive SelectiveShare = new(null, new StorageDriveData { Id = Guid.NewGuid(), SharingModel = DriveSharingModel.SelectiveShare });
    private static readonly StorageDrive Private = new(null, new StorageDriveData { Id = Guid.NewGuid(), SharingModel = DriveSharingModel.Private });
    private static readonly StorageDrive Transient = new(null, new StorageDriveData { Id = SystemDriveConstants.TransientTempDrive.Alias });

    private static EncryptedKeyHeader FileKey(byte[]? iv = null, byte[]? key = null) =>
        new() { EncryptionVersion = 1, Type = EncryptionType.FileKey, Iv = iv ?? ByteArrayUtil.GetRndByteArray(16), EncryptedAesKey = key! };

    [Test]
    public void AFileKeyIsOnlyASelectiveShareCommentsOrOnItsWayToOne()
    {
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertDriveTakes(SelectiveShare, EncryptionType.FileKey, FileSystemType.Comment), "a comment");
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertDriveTakes(Transient, EncryptionType.FileKey, FileSystemType.Comment), "on its way");
        AssertRefused(() => ZkDriveFiles.AssertDriveTakes(SelectiveShare, EncryptionType.FileKey, FileSystemType.Standard), "a file");
        AssertRefused(() => ZkDriveFiles.AssertDriveTakes(Private, EncryptionType.FileKey, FileSystemType.Comment), "another drive's comment");
    }

    [TestCase(EncryptionType.ZkDrive)]
    [TestCase(EncryptionType.Sealed)]
    [TestCase(EncryptionType.Aes)]
    [TestCase(EncryptionType.Share)]
    public void ASelectiveShareCommentTakesNoOtherKeyHeader(EncryptionType type)
    {
        AssertRefused(() => ZkDriveFiles.AssertDriveTakes(SelectiveShare, type, FileSystemType.Comment), $"{type}");
    }

    [Test]
    public void AFileKeyHoldsAStrongIvAndNoKey()
    {
        Assert.DoesNotThrow(() => FileKey().AssertValidFileKey(), "an IV alone");
        Assert.DoesNotThrow(() => FileKey(key: []).AssertValidFileKey(), "an empty key");
        AssertRefused(() => FileKey(key: ByteArrayUtil.GetRndByteArray(48)).AssertValidFileKey(), "with a key");
        AssertRefused(() => FileKey(iv: new byte[16]).AssertValidFileKey(), "a weak IV");
        AssertRefused(() => new EncryptedKeyHeader { EncryptionVersion = 2, Type = EncryptionType.FileKey, Iv = ByteArrayUtil.GetRndByteArray(16) }
            .AssertValidFileKey(), "another version");
    }

    [Test]
    public void AFileKeyIsPassedOnAsSentAndNeverOpened()
    {
        var fileKey = FileKey();
        Assert.That(fileKey.IsLockedByClient(), Is.True, "the server holds no key that opens it");

        var noKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
        var received = fileKey.DecryptOrPassSealed(ref noKey);
        Assert.That(received, Is.TypeOf<ClientLockedKeyHeader>(), "from a peer, as sent");
        Assert.That(EncryptedKeyHeader.EncryptKeyHeaderAes(received, ByteArrayUtil.GetRndByteArray(16), ref noKey), Is.SameAs(fileKey),
            "to a peer, as sent");
        Assert.Throws<OdinClientException>(() => fileKey.DecryptAesToKeyHeader(ref noKey), "never opened");
    }

    [Test]
    public void AShareLetsInByItsLiveRowsLowestCircleFirst()
    {
        var file = Guid.NewGuid();
        var (low, high) = (new Guid("00000000-0000-0000-0000-000000000001"), new Guid("ffffffff-0000-0000-0000-000000000001"));
        var shares = Shares(file, Row(file, high, DrivePermission.Read | DrivePermission.React), Row(file, low, DrivePermission.Read));

        Assert.That(shares.LiveRowOf(file)!.aclMemberId, Is.EqualTo(low), "the lowest circle id's, every time");
        Assert.That(shares.Grants(file, DrivePermission.React), Is.True, "any live row's bit");
        Assert.That(shares.Grants(file, DrivePermission.Comment), Is.False, "no row has it");
        Assert.That(shares.Names(file), Is.True);
        Assert.That((shares.LiveRowOf(Guid.NewGuid()), shares.Names(Guid.NewGuid())), Is.EqualTo(((DriveAclIndexRecord?)null, false)),
            "a file no row names them on");
    }

    [Test]
    public void ARowThatIsNotLiveNamesButLetsNoOneIn()
    {
        var (withdrawn, expired, noRead) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var shares = new CallerShares(new Dictionary<Guid, List<DriveAclIndexRecord>>
        {
            [withdrawn] = [Row(withdrawn, Guid.NewGuid(), 0)],
            [expired] = [Row(expired, Guid.NewGuid(), DrivePermission.Read | DrivePermission.React, UnixTimeUtc.Now().AddSeconds(-1))],
            [noRead] = [Row(noRead, Guid.NewGuid(), DrivePermission.React | DrivePermission.Comment)]
        }, UnixTimeUtc.Now());

        foreach (var (file, what) in new[] { (withdrawn, "withdrawn"), (expired, "expired"), (noRead, "without Read") })
        {
            Assert.That((shares.LiveRowOf(file), shares.Grants(file, DrivePermission.React), shares.Grants(file, DrivePermission.Comment),
                shares.Names(file)), Is.EqualTo(((DriveAclIndexRecord?)null, false, false, true)), what);
        }
    }

    [Test]
    public void AFileHeldBackAboveTheCallersLevelNamesButLetsNoOneIn()
    {
        var file = Guid.NewGuid();
        var shares = new CallerShares(new Dictionary<Guid, List<DriveAclIndexRecord>> { [file] = [Row(file, Guid.NewGuid(), DrivePermission.Read)] },
            UnixTimeUtc.Now(), new HashSet<Guid> { file });

        Assert.That((shares.LiveRowOf(file), shares.Grants(file, DrivePermission.Read), shares.Names(file)),
            Is.EqualTo(((DriveAclIndexRecord?)null, false, true)));
    }

    private static CallerShares Shares(Guid file, params DriveAclIndexRecord[] rows) =>
        new(new Dictionary<Guid, List<DriveAclIndexRecord>> { [file] = rows.ToList() }, UnixTimeUtc.Now());

    private static DriveAclIndexRecord Row(Guid file, Guid member, DrivePermission permissions, UnixTimeUtc? expiresAt = null) =>
        new() { fileId = file, aclMemberId = member, permissions = (int)permissions, expiresAt = expiresAt };

    private static void AssertRefused(TestDelegate check, string what) =>
        Assert.Throws<OdinClientException>(check, what);
}
