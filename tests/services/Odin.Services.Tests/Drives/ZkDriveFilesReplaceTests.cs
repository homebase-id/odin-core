using System;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// What a sealed key header may replace (docs/zero-knowledge-drive-api-plan.md, 2.4): another sealed one; or, from the
/// file's sender while no one else has changed it since their last write, the one its deposit was converted to
/// (docs/server-share-sealed-arrivals-plan.md, "A sender's sealed update").  Never anyone else's.
/// </summary>
public class ZkDriveFilesReplaceTests
{
    private static readonly OdinId Sam = new("sam.dotyou.cloud");
    private static readonly OdinId Merry = new("merry.dotyou.cloud");

    [TestCase(EncryptionType.Sealed, true, TestName = "ASealedKeyHeaderReplacesASealedOne")]
    [TestCase(EncryptionType.ZkDrive, false, TestName = "ASealedKeyHeaderNeverReplacesSomeoneElsesZkDriveOne")]
    [TestCase(EncryptionType.Aes, false, TestName = "ASealedKeyHeaderNeverReplacesSomeoneElsesAesOne")]
    public void WhatASealedKeyHeaderMayReplace(EncryptionType existing, bool allowed) =>
        AssertReplace(Encrypted(existing), Sam, allowed);

    [TestCase(EncryptionType.Aes, TestName = "ASendersSealedKeyHeaderReplacesTheAesOneTheServerConvertedTheirDepositTo")]
    [TestCase(EncryptionType.ZkDrive, TestName = "ASendersSealedKeyHeaderReplacesTheZkDriveOneADeviceConvertedTheirDepositTo")]
    public void ASendersSealedKeyHeaderReplacesTheirConvertedOne(EncryptionType existing) =>
        AssertReplace(Encrypted(existing, lastWrite: (Sam, current: true)), Sam, allowed: true);

    [Test]
    public void ASendersSealedKeyHeaderNeverReplacesAConvertedOneSomeoneElseChangedSince() =>
        AssertReplace(Encrypted(EncryptionType.Aes, lastWrite: (Sam, current: false)), Sam, allowed: false);

    [Test]
    public void ASealedKeyHeaderNeverReplacesAConvertedOneAnotherSenderWrote() =>
        AssertReplace(Encrypted(EncryptionType.Aes, lastWrite: (Sam, current: true)), Merry, allowed: false);

    [Test]
    public void ASealedKeyHeaderNeverReplacesTheOwnersFileFromItsLastWriterWhoDidNotSendIt() =>
        // A member who last edited the owner's shared file: their write is current, but the file isn't theirs
        AssertReplace(Encrypted(EncryptionType.ZkDrive, lastWrite: (Sam, current: true), lastWriterSentIt: false), Sam,
            allowed: false);

    [Test]
    public void ASendersSealedKeyHeaderOverTheirConvertedOneMustRotateItsIv()
    {
        var existing = Encrypted(EncryptionType.Aes, lastWrite: (Sam, current: true));
        var sameIv = new ClientLockedKeyHeader(new EncryptedKeyHeader
        {
            EncryptionVersion = 1, Type = EncryptionType.Sealed, Iv = existing.EncryptedKeyHeader.Iv,
            EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
        });

        Assert.Throws<OdinClientException>(() => ZkDriveFiles.AssertFitsUpdate(existing, sameIv, Sam), "the same IV under the same key");
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertFitsUpdate(existing, Sealed(), Sam), "a new IV");
    }

    [Test]
    public void ASealedKeyHeaderMayReplaceAnUnencryptedFile() =>
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertMayReplace(new ServerFileHeader
        {
            EncryptedKeyHeader = EncryptedKeyHeader.Empty(),
            FileMetadata = new FileMetadata { IsEncrypted = false }
        }, Sealed(), Sam));

    [TestCase(EncryptionType.Sealed, TestName = "AZkDriveKeyHeaderMayReplaceASealedOne")]
    [TestCase(EncryptionType.ZkDrive, TestName = "AZkDriveKeyHeaderMayReplaceAZkDriveOne")]
    public void AZkDriveKeyHeaderIsNotThisRulesConcern(EncryptionType existing) =>
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertMayReplace(Encrypted(existing),
            new ClientLockedKeyHeader(Locked(EncryptionType.ZkDrive)), Sam));

    private static void AssertReplace(ServerFileHeader existing, OdinId caller, bool allowed)
    {
        var replace = () => ZkDriveFiles.AssertMayReplace(existing, Sealed(), caller);
        if (allowed)
        {
            Assert.DoesNotThrow(() => replace());
            return;
        }

        var e = Assert.Throws<OdinClientException>(() => replace());
        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.DepositCannotBeUpdated), $"over {existing.EncryptedKeyHeader.Type}");
    }

    /// <param name="lastWrite">The record of a peer's last write, and whether the file still has the version tag it gave.</param>
    /// <param name="lastWriterSentIt">Whether the peer who last wrote it is its original sender; otherwise it is the owner's own.</param>
    private static ServerFileHeader Encrypted(EncryptionType type, (OdinId Sender, bool current)? lastWrite = null,
        bool lastWriterSentIt = true)
    {
        var versionTag = Guid.NewGuid();
        var originalSender = lastWriterSentIt ? lastWrite?.Sender : null;
        return new ServerFileHeader
        {
            EncryptedKeyHeader = Locked(type),
            FileMetadata = new FileMetadata
            {
                IsEncrypted = true, VersionTag = versionTag, SenderOdinId = originalSender?.DomainName
            },
            ServerMetadata = new ServerMetadata
            {
                SendersWrite = lastWrite is { } w
                    ? new SendersWrite { Sender = w.Sender, SendersVersionTag = Guid.NewGuid(), VersionTag = w.current ? versionTag : Guid.NewGuid() }
                    : null
            }
        };
    }

    private static ClientLockedKeyHeader Sealed() => new(Locked(EncryptionType.Sealed));

    private static EncryptedKeyHeader Locked(EncryptionType type) => new()
    {
        EncryptionVersion = 1,
        Type = type,
        Iv = ByteArrayUtil.GetRndByteArray(16),
        EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
    };
}
