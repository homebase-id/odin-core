using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Peer.Incoming.Drive.Query
{
    public class PeerDriveQueryService(DriveManager driveManager, IDriveFileSystem fileSystem)
    {
        public Task<QueryModifiedResult> QueryModified(FileQueryParams qp, QueryModifiedResultOptions options, OdinContext odinContext)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(qp.TargetDrive);
            var results = fileSystem.Query.GetModified(driveId, qp, options, odinContext);
            return results;
        }

        public Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request, OdinContext odinContext)
        {
            var results = fileSystem.Query.GetBatchCollection(request, odinContext);
            return results;
        }

        public Task<QueryBatchResult> QueryBatch(FileQueryParams qp, QueryBatchResultOptions options, OdinContext odinContext)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(qp.TargetDrive);
            var results = fileSystem.Query.GetBatch(driveId, qp, options, odinContext);
            return results;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(TargetDrive targetDrive, Guid fileId, OdinContext odinContext)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = odinContext.PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var result = await fileSystem.Storage.GetSharedSecretEncryptedHeader(file, odinContext);

            return result;
        }

        public async Task<(
                string encryptedKeyHeader64,
                bool IsEncrypted,
                PayloadDescriptor payloadDescriptor,
                PayloadStream ps)>
            GetPayloadStream(
                TargetDrive targetDrive,
                Guid fileId,
                string key,
                FileChunk chunk,
                OdinContext odinContext)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = odinContext.PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var (header, payloadDescriptor, encryptedKeyHeaderForPayload, fileExists) =
                await fileSystem.Storage.GetPayloadSharedSecretEncryptedKeyHeader(file, key, odinContext);

            if (!fileExists)
            {
                return (null, default, null, null);
            }

            string encryptedKeyHeader64 = encryptedKeyHeaderForPayload.ToBase64();

            var ps = await fileSystem.Storage.GetPayloadStream(file, key, chunk, odinContext);

            return (encryptedKeyHeader64, header.FileMetadata.IsEncrypted, payloadDescriptor, ps);
        }

        public async Task<(string encryptedKeyHeader64,
                bool IsEncrypted,
                PayloadDescriptor descriptor,
                string ContentType,
                UnixTimeUtc LastModified,
                Stream thumb)>
            GetThumbnail(TargetDrive targetDrive, Guid fileId, int height, int width, string payloadKey, OdinContext odinContext)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = odinContext.PermissionsContext.GetDriveId(targetDrive),
                FileId = fileId
            };

            var (header, payloadDescriptor, encryptedKeyHeaderForPayload, fileExists) =
                await fileSystem.Storage.GetPayloadSharedSecretEncryptedKeyHeader(file, payloadKey, odinContext);

            if (!fileExists)
            {
                return (null, default, null, null, default, null);
            }

            var thumbs = payloadDescriptor.Thumbnails?.ToList();
            var thumbnail = DriveFileUtility.FindMatchingThumbnail(thumbs, width, height, directMatchOnly: false);
            if (null == thumbnail)
            {
                return (null, default, null, null, default, null);
            }

            var (thumb, _) = await fileSystem.Storage.GetThumbnailPayloadStream(file, width, height, payloadKey, payloadDescriptor.Uid, odinContext);
            string encryptedKeyHeader64 = encryptedKeyHeaderForPayload.ToBase64();
            return (encryptedKeyHeader64, header.FileMetadata.IsEncrypted, payloadDescriptor, thumbnail.ContentType, payloadDescriptor.LastModified, thumb);
        }

        public async Task<IEnumerable<PerimeterDriveData>> GetDrives(Guid driveType, OdinContext odinContext)
        {
            //filter drives by only returning those the caller can see
            var allDrives = await driveManager.GetDrives(driveType, PageOptions.All, odinContext);
            var perms = odinContext.PermissionsContext;
            var readableDrives = allDrives.Results.Where(drive => perms.HasDrivePermission(drive.Id, DrivePermission.Read));
            return readableDrives.Select(drive => new PerimeterDriveData()
            {
                TargetDrive = drive.TargetDriveInfo,
            });
        }
    }
}