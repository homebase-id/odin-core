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

/// <summary>Files on the Shard Recovery drive, as the zero-knowledge drive tests write and look them up.</summary>
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
    /// <paramref name="sender"/> (Frodo, or an app of his) writes a deposit to his transient drive and sends it to
    /// Sam's Shard Recovery drive.  Sending it again names <paramref name="versionTag"/>, the one Sam's copy has
    /// (docs/file-access.md, "Changing a file you sent").
    /// </summary>
    public static async Task<(KeyHeader FileKey, EncryptedKeyHeader Sealed)> SendDepositAsync(IV2Caller sender,
        OwnerSession frodo, OwnerSession sam, string publicKeyJwk, Guid uniqueId, string content,
        TestPayloadDefinition? payload = null, TargetDrive? remoteDrive = null, Guid? groupId = null, List<ShareRequest>? shares = null,
        int fileType = 100, Guid? versionTag = null)
    {
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var metadata = Metadata(uniqueId, content, fileType: fileType);
        metadata.AllowDistribution = true;
        metadata.VersionTag = versionTag;
        metadata.AppData.GroupId = groupId;
        UploadManifest? manifest = null;
        if (payload != null)
        {
            payload.Iv = ByteArrayUtil.GetRndByteArray(16);
            manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] };
        }

        var (upload, _, _, _) = await sender.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new TransitOptions
            {
                IsTransient = true,
                Recipients = [sam.Identity.DomainName],
                RemoteTargetDrive = remoteDrive ?? Drive
            }, manifest, payload == null ? null : [payload], keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader, shares: shares);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await frodo.Sync.DrainOutboxAsync();
        return (fileKey, sealedKeyHeader);
    }
}
