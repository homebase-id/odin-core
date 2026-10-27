#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>Files on the Shard Recovery drive, as the zero-knowledge drive tests write and look them up.</summary>
internal static class ZkTestFiles
{
    public static readonly TargetDrive Drive = WellKnownAppDrives.ShardRecoveryDrive;

    public static UploadFileMetadata Metadata(Guid uniqueId, string content, Guid? versionTag = null)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, content, AccessControlList.OwnerOnly);
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
        KeyHeader keyHeader, EncryptedKeyHeader locked, Guid versionTag, List<OdinId>? recipients = null)
    {
        var metadata = Metadata(uniqueId, "updated", versionTag);
        metadata.AppData.Content = keyHeader.EncryptDataAes(metadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;

        return owner.Drives.Writer.UpdateFileByFileId(Drive.Alias, fileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] },
            Recipients = recipients
        }, metadata, [], locked);
    }
}
