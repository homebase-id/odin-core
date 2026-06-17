using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Storage.Cache;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
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
    /// <summary>
    /// Owner-side perimeter logic for the temporal (time-boxed) read API. Mirrors <see cref="PeerDriveQueryService"/>
    /// but routes every read through the temporal file-system methods, which assert
    /// <see cref="DrivePermission.ConditionalTemporalRead"/> and clamp results to a recent window. Each successful
    /// (genuinely time-boxed) read fires a throttled <see cref="TemporalDriveAccessedNotification"/> to the owner.
    /// </summary>
    public class PeerTemporalDriveQueryService(
        IDriveManager driveManager,
        IDriveFileSystem fileSystem,
        IMediator mediator,
        ITenantLevel2Cache<PeerTemporalDriveQueryService> cache)
    {
        private static readonly TimeSpan NotifyThrottle = TimeSpan.FromHours(1);

        /// <summary>
        /// Lightweight preflight: does the caller currently have temporal (or full) read access to the drive,
        /// and does the drive exist? Reads no data and raises no access notification.
        /// </summary>
        public async Task<TemporalAccessStatus> VerifyAccessAsync(TargetDrive targetDrive, IOdinContext odinContext)
        {
            var driveId = targetDrive.Alias;
            var drive = await driveManager.GetDriveAsync(driveId);
            if (drive == null)
            {
                return new TemporalAccessStatus { HasAccess = false, TargetDrive = targetDrive };
            }

            var perms = odinContext.PermissionsContext;
            var hasAccess = perms.HasDrivePermission(driveId, DrivePermission.Read) ||
                            perms.HasDrivePermission(driveId, DrivePermission.ConditionalTemporalRead);

            return new TemporalAccessStatus
            {
                HasAccess = hasAccess,
                TargetDrive = drive.TargetDriveInfo,
                WindowSeconds = hasAccess ? TemporalReadPolicy.ResolveWindowSeconds(odinContext, drive) : null
            };
        }

        public async Task<QueryBatchResult> QueryBatch(FileQueryParamsV1 qp, QueryBatchResultOptions options, IOdinContext odinContext)
        {
            var results = await fileSystem.Query.GetTemporalBatch(qp.DriveId, qp, options, odinContext);
            await MaybeNotifyAsync(qp.DriveId, odinContext);
            return results;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(TargetDrive targetDrive, Guid fileId, IOdinContext odinContext)
        {
            var file = new InternalDriveFileId
            {
                DriveId = targetDrive.Alias,
                FileId = fileId
            };

            var result = await fileSystem.Storage.GetTemporalSharedSecretEncryptedHeader(file, odinContext);
            if (result != null)
            {
                await MaybeNotifyAsync(file.DriveId, odinContext);
            }

            return result;
        }

        public async Task<(
                string encryptedKeyHeader64,
                bool IsEncrypted,
                PayloadDescriptor payloadDescriptor,
                PayloadStream ps)>
            GetPayloadStreamAsync(TargetDrive targetDrive, Guid fileId, string key, FileChunk chunk, IOdinContext odinContext)
        {
            var file = new InternalDriveFileId
            {
                DriveId = targetDrive.Alias,
                FileId = fileId
            };

            var (header, payloadDescriptor, encryptedKeyHeaderForPayload, fileExists) =
                await fileSystem.Storage.GetTemporalPayloadSharedSecretEncryptedKeyHeaderAsync(file, key, odinContext);

            if (!fileExists)
            {
                return (null, default, null, null);
            }

            var encryptedKeyHeader64 = encryptedKeyHeaderForPayload.ToBase64();

            // NOTE: caller takes ownership of ps and is responsible for disposing
            var ps = await fileSystem.Storage.GetTemporalPayloadStreamAsync(file, key, chunk, odinContext);

            await MaybeNotifyAsync(file.DriveId, odinContext);
            return (encryptedKeyHeader64, header.FileMetadata.IsEncrypted, payloadDescriptor, ps);
        }

        public async Task<(string encryptedKeyHeader64,
                bool IsEncrypted,
                PayloadDescriptor descriptor,
                string ContentType,
                UnixTimeUtc LastModified,
                Stream thumb)>
            GetThumbnailAsync(TargetDrive targetDrive, Guid fileId, int height, int width, string payloadKey, IOdinContext odinContext)
        {
            var file = new InternalDriveFileId
            {
                DriveId = targetDrive.Alias,
                FileId = fileId
            };

            var (header, payloadDescriptor, encryptedKeyHeaderForPayload, fileExists) =
                await fileSystem.Storage.GetTemporalPayloadSharedSecretEncryptedKeyHeaderAsync(file, payloadKey, odinContext);

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

            var (thumb, _) =
                await fileSystem.Storage.GetTemporalThumbnailPayloadStreamAsync(file, width, height, payloadKey, payloadDescriptor.Uid,
                    odinContext);
            var encryptedKeyHeader64 = encryptedKeyHeaderForPayload.ToBase64();

            await MaybeNotifyAsync(file.DriveId, odinContext);
            return (encryptedKeyHeader64, header.FileMetadata.IsEncrypted, payloadDescriptor, thumbnail.ContentType,
                payloadDescriptor.LastModified, thumb);
        }

        /// <summary>
        /// Fires a throttled access notification to the owner — but only for genuinely time-boxed access
        /// (a full-read caller using this API is not an "emergency" read and is not reported).
        /// </summary>
        private async Task MaybeNotifyAsync(Guid driveId, IOdinContext odinContext)
        {
            var drive = await driveManager.GetDriveAsync(driveId);
            if (drive == null)
            {
                return;
            }

            if (TemporalReadPolicy.ResolveWindowSeconds(odinContext, drive) == null)
            {
                // Caller holds full read access; this is not a time-boxed/emergency read.
                return;
            }

            var caller = odinContext.GetCallerOdinIdOrFail();
            var cacheKey = $"temporal-access:{caller}:{driveId}";

            var seen = await cache.TryGetAsync<bool>(cacheKey);
            if (seen.HasValue)
            {
                return;
            }

            await cache.SetAsync(cacheKey, true, NotifyThrottle);
            await mediator.Publish(new TemporalDriveAccessedNotification
            {
                Accessor = caller,
                Drive = drive.TargetDriveInfo,
                OdinContext = odinContext
            });
        }
    }
}
