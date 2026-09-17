using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

/// <summary>
/// The one file the anonymous-caller fixtures in this folder upload, and the anonymous-readable
/// drive they put it on.
/// </summary>
/// <remarks>
/// <see cref="DriveQueryTests"/> and <see cref="DriveStorageTests"/> each carried a byte-identical
/// copy of this metadata literal and drive-create pair — same content string, same file / data types,
/// same zero user date — differing only in the names of the tuple elements they returned. Both
/// fixtures are about what a no-credential caller can see on an anonymous-readable drive, so the
/// shape belongs to the folder rather than to either of them.
/// </remarks>
internal static class AnonymousDriveUploads
{
    /// <summary>The metadata literal both fixtures upload, carried verbatim from the originals.</summary>
    public static UploadFileMetadata Metadata(Guid tag, AccessControlList acl, Guid? versionTag = null) =>
        new()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            VersionTag = versionTag,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = new List<Guid> { tag }
            },
            AccessControlList = acl
        };

    /// <summary>Creates a fresh anonymous-readable drive and puts one <see cref="Metadata"/> file on it.</summary>
    public static async Task<(UploadResult UploadResult, UploadFileMetadata UploadedFileMetadata)> UploadToNewDriveAsync(
        OwnerSession owner, Guid tag, SecurityGroupType requiredSecurityGroup)
    {
        var uploadFileMetadata = Metadata(tag, new AccessControlList { RequiredSecurityGroup = requiredSecurityGroup });

        var td = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(td, "a drive", allowAnonymousReads: true);
        var response = await owner.V1.Drive.UploadNewMetadata(td, uploadFileMetadata);
        return (response.Content, uploadFileMetadata);
    }
}
