using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// What a sealed key header may replace (docs/zero-knowledge-drive-api-plan.md, 2.4): only another sealed one.
/// A file a key holder took over (zkDrive) never goes back to sealed, and an aes file never loses the key only the
/// server could open.
/// </summary>
public class ZkDriveFilesReplaceTests
{
    [TestCase(EncryptionType.Sealed, true, TestName = "ASealedKeyHeaderReplacesASealedOne")]
    [TestCase(EncryptionType.ZkDrive, false, TestName = "ASealedKeyHeaderNeverReplacesAZkDriveOne")]
    [TestCase(EncryptionType.Aes, false, TestName = "ASealedKeyHeaderNeverReplacesAnAesOne")]
    public void WhatASealedKeyHeaderMayReplace(EncryptionType existing, bool allowed)
    {
        var replace = () => ZkDriveFiles.AssertMayReplace(Encrypted(existing), Sealed());
        if (allowed)
        {
            Assert.DoesNotThrow(() => replace());
            return;
        }

        var e = Assert.Throws<OdinClientException>(() => replace());
        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.DepositCannotBeUpdated), $"over {existing}");
    }

    [Test]
    public void ASealedKeyHeaderMayReplaceAnUnencryptedFile() =>
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertMayReplace(new ServerFileHeader
        {
            EncryptedKeyHeader = EncryptedKeyHeader.Empty(),
            FileMetadata = new FileMetadata { IsEncrypted = false }
        }, Sealed()));

    [TestCase(DriveSharingModel.Private)]
    [TestCase(DriveSharingModel.SelectiveShare)]
    public void ASealedReplacementCarriesPayloadsWhereADepositIsTaken(DriveSharingModel model) =>
        // One set of deposit rules for every drive that takes one (docs/server-share-sealed-arrivals-plan.md, "Decisions")
        Assert.DoesNotThrow(() => Deposits.AssertTakesNoPayloads(Sealed(), addsPayloads: true, TestDrives.Of(model)));

    [Test]
    public void ASealedReplacementCarriesNoPayloadsWhereNoDepositIsTaken()
    {
        // A Server Share drive with no write-only key pair takes no deposit
        var drive = TestDrives.Of(DriveSharingModel.Collaborative);
        var e = Assert.Throws<OdinClientException>(() => Deposits.AssertTakesNoPayloads(Sealed(), addsPayloads: true, drive));
        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.InvalidPayload));
        Assert.DoesNotThrow(() => Deposits.AssertTakesNoPayloads(Sealed(), addsPayloads: false, drive));
    }

    [TestCase(EncryptionType.Sealed, TestName = "AZkDriveKeyHeaderMayReplaceASealedOne")]
    [TestCase(EncryptionType.ZkDrive, TestName = "AZkDriveKeyHeaderMayReplaceAZkDriveOne")]
    public void AZkDriveKeyHeaderIsNotThisRulesConcern(EncryptionType existing) =>
        Assert.DoesNotThrow(() => ZkDriveFiles.AssertMayReplace(Encrypted(existing), new ClientLockedKeyHeader(Locked(EncryptionType.ZkDrive))));

    private static ServerFileHeader Encrypted(EncryptionType type) => new()
    {
        EncryptedKeyHeader = Locked(type),
        FileMetadata = new FileMetadata { IsEncrypted = true }
    };

    private static ClientLockedKeyHeader Sealed() => new(Locked(EncryptionType.Sealed));

    private static EncryptedKeyHeader Locked(EncryptionType type) => new()
    {
        EncryptionVersion = 1,
        Type = type,
        Iv = ByteArrayUtil.GetRndByteArray(16),
        EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
    };
}
