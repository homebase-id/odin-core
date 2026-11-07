#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.Shares;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using NUnit.Framework;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Files as the zero-knowledge drive tests write, send and look them up: on the Shard Recovery drive unless a test names
/// another.
/// </summary>
internal static class ZkTestFiles
{
    public static readonly TargetDrive Drive = WellKnownAppDrives.ShardRecoveryDrive;

    public static UploadFileMetadata Metadata(Guid uniqueId, string content, Guid? versionTag = null, int fileType = 100)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType, content, AccessControlList.OwnerOnly);
        metadata.AppData.UniqueId = uniqueId;
        metadata.VersionTag = versionTag;
        return metadata;
    }

    public static QueryBatchRequest QueryFor(Guid uniqueId) => new()
    {
        QueryParams = new FileQueryParamsV1 { ClientUniqueIdAtLeastOne = [uniqueId] },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
    };

    /// <summary>The header of <paramref name="caller"/>'s file with <paramref name="uniqueId"/>, which must be found.</summary>
    public static async Task<SharedSecretEncryptedFileHeader> HeaderByUniqueIdAsync(IV2Caller caller, Guid uniqueId, TargetDrive? drive = null)
    {
        var read = await caller.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, (drive ?? Drive).Alias);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        return read.Content!;
    }

    /// <summary>The same file key with a new IV, as an encrypted update needs.</summary>
    public static KeyHeader WithNewIv(KeyHeader fileKey) => new() { Iv = ByteArrayUtil.GetRndByteArray(16), AesKey = fileKey.AesKey };

    /// <summary>
    /// An update whose content, "updated", is encrypted under <paramref name="keyHeader"/>, with <paramref name="locked"/> as
    /// its key header.
    /// </summary>
    public static Task<Refit.ApiResponse<UpdateFileResult>> UpdateAsync(OwnerSession owner, Guid fileId, Guid uniqueId,
        KeyHeader keyHeader, EncryptedKeyHeader locked, Guid versionTag, List<OdinId>? recipients = null, TargetDrive? drive = null)
    {
        var metadata = Metadata(uniqueId, "updated", versionTag);
        metadata.AppData.Content = keyHeader.EncryptDataAes(metadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;

        return owner.Drives.Writer.UpdateFileByFileId((drive ?? Drive).Alias, fileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] },
            Recipients = recipients
        }, metadata, [], locked);
    }

    /// <summary>
    /// <paramref name="sender"/> (an owner, or an app of <paramref name="sendersOwner"/>'s) writes a deposit to the transient
    /// drive and sends it to <paramref name="recipient"/>'s <paramref name="remoteDrive"/>, the Shard Recovery drive unless
    /// named.  Sending it again names <paramref name="versionTag"/>, the one the recipient's copy has (docs/file-access.md,
    /// "Changing a file you sent").
    /// </summary>
    public static async Task<(KeyHeader FileKey, EncryptedKeyHeader Sealed)> SendDepositAsync(IV2Caller sender,
        OwnerSession sendersOwner, OwnerSession recipient, string publicKeyJwk, Guid uniqueId, string content,
        TestPayloadDefinition? payload = null, TargetDrive? remoteDrive = null, Guid? groupId = null, List<ShareRequest>? shares = null,
        int fileType = 100, Guid? versionTag = null)
    {
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var metadata = Metadata(uniqueId, content, versionTag, fileType);
        metadata.AppData.GroupId = groupId;
        UploadManifest? manifest = null;
        if (payload != null)
        {
            payload.Iv = ByteArrayUtil.GetRndByteArray(16);
            manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] };
        }

        await SendFromTransientAsync(sender, sendersOwner, recipient, remoteDrive ?? Drive, metadata, manifest, payload == null ? null : [payload],
            fileKey, sealedKeyHeader, shares);
        return (fileKey, sealedKeyHeader);
    }

    /// <summary>
    /// <paramref name="sender"/>'s ordinary encrypted file, locked with their shared secret on their transient drive, sent to
    /// <paramref name="recipient"/>'s <paramref name="drive"/>, under <paramref name="fileKey"/> or a new one.  Sending it
    /// again names <paramref name="versionTag"/>, the one the recipient's copy has.  Returns the file key.
    /// </summary>
    public static async Task<KeyHeader> SendUnsealedAsync(OwnerSession sender, OwnerSession recipient, TargetDrive drive, Guid uniqueId,
        string content, KeyHeader? fileKey = null, Guid? versionTag = null)
    {
        fileKey ??= KeyHeader.NewRandom16();
        await SendFromTransientAsync(sender, sender, recipient, drive, Metadata(uniqueId, content, versionTag), null, null, fileKey,
            null, null);
        return fileKey;
    }

    /// <summary>A new Server Share drive of <paramref name="owner"/>'s, marked as a community channel when asked.</summary>
    public static async Task<TargetDrive> CreateServerShareDriveAsync(OwnerSession owner, bool communityChannel = false)
    {
        var drive = TargetDrive.NewTargetDrive();
        var create = await owner.Admin.CreateDrive(drive, $"{owner.Identity}'s shared", allowAnonymousReads: false,
            allowSubscriptions: communityChannel, attributes: communityChannel ? DriveSpec.CollabAttributes : null);
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        return drive;
    }

    /// <summary>The content of <paramref name="header"/>, as its owner's client opens it with their shared secret.</summary>
    public static string ContentAsOwner(OwnerSession owner, SharedSecretEncryptedFileHeader header)
    {
        var sharedSecret = owner.SharedSecret;
        return header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret)
            .Decrypt(header.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes();
    }

    private static async Task SendFromTransientAsync(IV2Caller sender, OwnerSession sendersOwner, OwnerSession recipient,
        TargetDrive drive, UploadFileMetadata metadata, UploadManifest? manifest, List<TestPayloadDefinition>? payloads, KeyHeader fileKey,
        EncryptedKeyHeader? lockedKeyHeader, List<ShareRequest>? shares)
    {
        metadata.AllowDistribution = true;
        var (upload, _, _, _) = await sender.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new TransitOptions
            {
                IsTransient = true,
                Recipients = [recipient.Identity.DomainName],
                RemoteTargetDrive = drive
            }, manifest, payloads, keyHeader: fileKey, lockedKeyHeader: lockedKeyHeader, shares: shares);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await sendersOwner.Sync.DrainOutboxAsync();
    }
}
