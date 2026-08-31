using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Ttl;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase(
        ILoggerFactory loggerFactory,
        IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        IDriveManager driveManager,
        LongTermStorageManager longTermStorageManager,
        UploadStorageManager uploadStorageManager,
        InboxStorageManager inboxStorageManager,
        IdentityDatabase db,
        InboxFileStore inboxFileStore,
        UploadFileStore uploadFileStore,
        FileExpiryScheduler fileExpiryScheduler) : RequirePermissionsBase
    {
        private readonly ILogger<DriveStorageServiceBase> _logger = loggerFactory.CreateLogger<DriveStorageServiceBase>();

        protected override IDriveManager DriveManager { get; } = driveManager;

        private IDriveFileStore ResolveStore(StagingArea area) => area switch
        {
            StagingArea.Upload => uploadFileStore,
            StagingArea.Inbox  => inboxFileStore, // TODO:INBOX drop this arm with the folder-based inbox
            _ => throw new ArgumentOutOfRangeException(nameof(area))
        };

        private static string StagingRoot(StagingArea area, StorageDrive drive) => area switch
        {
            StagingArea.Upload => drive.GetDriveUploadPath(),
            StagingArea.Inbox  => drive.GetDriveInboxPath(), // TODO:INBOX drop this arm with the folder-based inbox
            _ => throw new ArgumentOutOfRangeException(nameof(area))
        };

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> of which the inheriting class manages
        /// </summary>
        public abstract FileSystemType GetFileSystemType();

        public async Task<SharedSecretEncryptedFileHeader> GetSharedSecretEncryptedHeader(InternalDriveFileId file,
            IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext);
            if (serverFileHeader == null)
            {
                return null;
            }

            var result = DriveFileUtility.CreateClientFileHeader(serverFileHeader, odinContext);
            return result;
        }

        /// <summary>
        /// Gets an EncryptedKeyHeader for a given payload using the payload's IV
        /// </summary>
        public async Task<(ServerFileHeader header, PayloadDescriptor payloadDescriptor, EncryptedKeyHeader encryptedKeyHeader, bool
                fileExists)>
            GetPayloadSharedSecretEncryptedKeyHeaderAsync(InternalDriveFileId file, string payloadKey, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext);
            if (serverFileHeader == null)
            {
                return (null, null, null, false);
            }

            var payloadDescriptor = serverFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                return (null, null, null, false);
            }

            EncryptedKeyHeader payloadEncryptedKeyHeader = odinContext.Caller.ClientTokenType == ClientTokenType.Cdn
                ? null
                : DriveFileUtility.GetPayloadEncryptedKeyHeader(
                    serverFileHeader,
                    payloadDescriptor,
                    odinContext);

            return (serverFileHeader, payloadDescriptor, payloadEncryptedKeyHeader, true);
        }

        // =====================================================================================
        // Temporal (time-boxed) read API.
        //
        // These methods are the ONLY read path that honors DrivePermission.ConditionalTemporalRead.
        // They assert the temporal flag (NOT Read) and drop any file whose server-set modified date is
        // older than the resolved window cutoff. The normal read methods above intentionally reject the
        // temporal flag, so a caller holding only that flag can read exclusively through here.
        // =====================================================================================

        private async Task<ServerFileHeader> GetTemporalServerFileHeader(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            odinContext.PermissionsContext.AssertCanConditionalTemporalReadDrive(file.DriveId);

            // GetServerFileHeaderInternal enforces the per-file ACL.
            var header = await GetServerFileHeaderInternal(file, odinContext);
            if (header == null)
            {
                return null;
            }

            AssertValidFileSystemType(header.ServerMetadata);

            // Time-window clamp on the server-set modified timestamp (FileMetadata.Updated). Out-of-window
            // files are reported as missing (null -> 404) with no distinguishable signal.
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var cutoff = TemporalReadPolicy.ResolveCutoff(odinContext, drive);
            if (cutoff != null && header.FileMetadata.Updated < cutoff.Value)
            {
                return null;
            }

            return header;
        }

        public async Task<SharedSecretEncryptedFileHeader> GetTemporalSharedSecretEncryptedHeader(InternalDriveFileId file,
            IOdinContext odinContext)
        {
            var serverFileHeader = await this.GetTemporalServerFileHeader(file, odinContext);
            if (serverFileHeader == null)
            {
                return null;
            }

            return DriveFileUtility.CreateClientFileHeader(serverFileHeader, odinContext);
        }

        public async Task<(ServerFileHeader header, PayloadDescriptor payloadDescriptor, EncryptedKeyHeader encryptedKeyHeader, bool
                fileExists)>
            GetTemporalPayloadSharedSecretEncryptedKeyHeaderAsync(InternalDriveFileId file, string payloadKey, IOdinContext odinContext)
        {
            var serverFileHeader = await this.GetTemporalServerFileHeader(file, odinContext);
            if (serverFileHeader == null)
            {
                return (null, null, null, false);
            }

            var payloadDescriptor = serverFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                return (null, null, null, false);
            }

            EncryptedKeyHeader payloadEncryptedKeyHeader = odinContext.Caller.ClientTokenType == ClientTokenType.Cdn
                ? null
                : DriveFileUtility.GetPayloadEncryptedKeyHeader(serverFileHeader, payloadDescriptor, odinContext);

            return (serverFileHeader, payloadDescriptor, payloadEncryptedKeyHeader, true);
        }

        public async Task<PayloadStream> GetTemporalPayloadStreamAsync(InternalDriveFileId file, string key, FileChunk chunk,
            IOdinContext odinContext)
        {
            TenantPathManager.AssertValidPayloadKey(key);

            // Asserts the temporal flag + applies the window clamp before any payload bytes are read.
            var header = await GetTemporalServerFileHeader(file, odinContext);
            if (header == null)
            {
                return null;
            }

            var descriptor = header.FileMetadata.GetPayloadDescriptor(key);
            if (descriptor == null)
            {
                return null;
            }

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            try
            {
                var stream = await longTermStorageManager.GetPayloadStreamAsync(drive, file.FileId, descriptor, chunk);
                return new PayloadStream(descriptor, stream.Length, stream);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetTemporalThumbnailPayloadStreamAsync(
            InternalDriveFileId file, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid, IOdinContext odinContext,
            bool directMatchOnly = false)
        {
            TenantPathManager.AssertValidPayloadKey(payloadKey);

            var header = await GetTemporalServerFileHeader(file, odinContext);
            var thumbs = header?.FileMetadata.GetPayloadDescriptor(payloadKey)?.Thumbnails?.ToList();
            if (null == thumbs || !thumbs.Any())
            {
                return (Stream.Null, null);
            }

            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            var directMatchingThumb = thumbs.SingleOrDefault(t => t.PixelHeight == height && t.PixelWidth == width);
            if (null != directMatchingThumb)
            {
                try
                {
                    var s = await longTermStorageManager.GetThumbnailStreamAsync(drive, file.FileId, width, height, payloadKey, payloadUid);
                    return (s, directMatchingThumb);
                }
                catch (Exception)
                {
                    if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
                    {
                        return (Stream.Null, directMatchingThumb);
                    }

                    throw;
                }
            }

            var nextSizeUp = DriveFileUtility.FindMatchingThumbnail(thumbs, width, height, directMatchOnly);
            if (null == nextSizeUp)
            {
                return (Stream.Null, null);
            }

            try
            {
                var stream = await longTermStorageManager.GetThumbnailStreamAsync(
                    drive, file.FileId, nextSizeUp.PixelWidth, nextSizeUp.PixelHeight, payloadKey, payloadUid);
                return (stream, nextSizeUp);
            }
            catch (Exception)
            {
                if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
                {
                    return (Stream.Null, nextSizeUp);
                }

                throw;
            }
        }

        public async Task<InternalDriveFileId> CreateInternalFileId(Guid driveId, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(driveId, odinContext);
            var df = new InternalDriveFileId()
            {
                FileId = longTermStorageManager.CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        public async Task<(int originalRecipientCount, PagedResult<RecipientTransferHistoryItem> results)> GetTransferHistory(
            InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext);
            if (serverFileHeader == null)
            {
                return (0, null);
            }

            var results = await longTermStorageManager.GetTransferHistory(file.DriveId, file.FileId);

            var pagedResults = new PagedResult<RecipientTransferHistoryItem>(PageOptions.All, 1, results);
            return (serverFileHeader.ServerMetadata.OriginalRecipientCount, pagedResults);
        }

        private async Task UpdateActiveFileHeaderInternal(InternalDriveFileId targetFile, ServerFileHeader header,
            IOdinContext odinContext,
            bool raiseEvent = false, bool ignoreFeedDistribution = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException(
                    "An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            //short circuit
            var fileExists = await FileExists(targetFile, odinContext);
            if (!fileExists)
            {
                await WriteNewFileHeader(targetFile, header, odinContext, raiseEvent);
                return;
            }

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (metadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext);
            metadata.GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId;
            metadata.FileState = existingHeader.FileMetadata.FileState;
            metadata.SenderOdinId = existingHeader.FileMetadata.SenderOdinId;
            metadata.OriginalAuthor = existingHeader.FileMetadata.OriginalAuthor;
            ApplyShortenOnlyTtl(existingHeader.FileMetadata, metadata);

            // WriteFileHeaderInternal sets the Created / Updated on header.FileMetadata
            await AssertPayloadsExistOnFileSystemAsync(header);
            await WriteFileHeaderInternal(header, odinContext);

            //HACKed in for Feed drive -> Should become a data subscription check
            if (raiseEvent)
            {
                if (await TryShouldRaiseDriveEventAsync(targetFile))
                {
                    await TryPublishAsync(new DriveFileChangedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        IgnoreFeedDistribution = ignoreFeedDistribution
                    });
                }
            }
        }

        /// <summary>
        /// Writes a new file header w/o checking for an existing one
        /// </summary>
        public async Task WriteNewFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext,
            bool raiseEvent = false, Guid? useThisVersionTag = null)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException(
                    "An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded
            metadata.FileState = FileState.Active;

            await WriteFileHeaderInternal(header, odinContext, useThisVersionTag); // sets the header.FileMetadata.Created/Updated

            //HACKed in for Feed drive -> should become data subscription
            if (raiseEvent)
            {
                if (await TryShouldRaiseDriveEventAsync(targetFile))
                {
                    await TryPublishAsync(new DriveFileAddedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                    });
                }
            }
        }

        public async Task<uint> WriteUploadStream(InternalDriveFileId file, string extension, Stream stream, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            return await uploadStorageManager.WriteUploadStream(file, extension, stream);
        }

        // TODO:INBOX No production callers remain: the peer-transfer path streams to long-term and the feed path
        // now carries metadata on the inbox row. Only InboxStorageManagerTests still exercises it. Delete together
        // with the folder-based inbox.
        public async Task<uint> WriteInboxStream(InternalDriveFileId file, string extension, Stream stream, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            return await inboxStorageManager.WriteInboxStream(file, extension, stream);
        }

        /// <summary>
        /// Streams an incoming peer payload/thumbnail straight into long-term storage under the (incoming)
        /// <paramref name="file"/> fileId, bypassing the inbox staging folder. Used by the peer receive path so
        /// that inbox processing has nothing to copy at commit time (see <see cref="StagingArea.LongTerm"/>).
        /// <paramref name="extension"/> is the staging-style payload/thumbnail extension.
        /// </summary>
        public async Task<uint> WritePayloadDirectlyToLongTerm(InternalDriveFileId file, string extension, Stream stream,
            IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            return await longTermStorageManager.WriteStreamDirectToLongTermAsync(drive, file.FileId, extension, stream);
        }

        /// <summary>
        /// Reads the whole file so be sure this is only used on small'ish files; ones you're ok with loaded fully into server-memory
        /// </summary>
        // TODO:INBOX Reads from the folder-based inbox; delete once the inbox folder is drained.
        public async Task<byte[]> GetAllFileBytesFromInboxFile(InternalDriveFileId file, string extension, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadDriveAsync(file.DriveId, odinContext);
            return await inboxStorageManager.GetAllInboxFileBytes(file, extension);
        }

        public async Task<bool> UploadFileExists(InternalDriveFileId file, string extension, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            odinContext.Caller.AssertCallerIsOwner();
            return await uploadStorageManager.UploadFileExists(file, extension);
        }

        // TODO:INBOX Backs the test-only owner endpoint over the folder-based inbox; delete once the folder is drained.
        public async Task<bool> InboxFileExists(InternalDriveFileId file, string extension, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            odinContext.Caller.AssertCallerIsOwner();
            return await inboxStorageManager.InboxFileExists(file, extension);
        }

        /// <summary>
        /// Tests if additional payload or thumbnail files for the given file
        /// </summary>
        public async Task<bool> HasOrphanPayloads(InternalDriveFileId file, IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsOwner();
            var originalHeader = await this.GetServerFileHeaderInternal(file, odinContext);
            var metadata = originalHeader.FileMetadata;
            // return await orphanTestUtil.HasOrphanPayloadsOrThumbnails(file, metadata.Payloads);
            return false;
        }

        public async Task<byte[]> GetAllFileBytesFromTempFileForWriting(InternalDriveFileId file, string extension,
            StagingArea sourceArea, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var store = ResolveStore(sourceArea);
            var path = Path.Combine(StagingRoot(sourceArea, drive), TenantPathManager.GetFilename(file.FileId, extension));
            return await store.ReadAllBytesAsync(path);
        }

        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetThumbnailPayloadStreamAsync(InternalDriveFileId file,
            int width,
            int height,
            string payloadKey, UnixTimeUtcUnique payloadUid, IOdinContext odinContext, bool directMatchOnly = false)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadDriveAsync(file.DriveId, odinContext);

            TenantPathManager.AssertValidPayloadKey(payloadKey);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext);
            var thumbs = header?.FileMetadata.GetPayloadDescriptor(payloadKey)?.Thumbnails?.ToList();
            if (null == thumbs || !thumbs.Any())
            {
                return (Stream.Null, null);
            }

            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            var directMatchingThumb = thumbs.SingleOrDefault(t => t.PixelHeight == height && t.PixelWidth == width);
            if (null != directMatchingThumb)
            {
                try
                {
                    var s = await longTermStorageManager.GetThumbnailStreamAsync(drive, file.FileId, width, height, payloadKey, payloadUid);
                    return (s, directMatchingThumb);
                }
                catch (Exception)
                {
                    if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
                    {
                        return (Stream.Null, directMatchingThumb);
                    }

                    throw;
                }
            }

            var nextSizeUp = DriveFileUtility.FindMatchingThumbnail(thumbs, width, height, directMatchOnly);
            if (null == nextSizeUp)
            {
                return (Stream.Null, null);
            }

            try
            {
                var stream = await longTermStorageManager.GetThumbnailStreamAsync(
                    drive,
                    file.FileId,
                    nextSizeUp.PixelWidth,
                    nextSizeUp.PixelHeight,
                    payloadKey, payloadUid);

                return (stream, nextSizeUp);
            }
            catch (Exception)
            {
                if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
                {
                    return (Stream.Null, nextSizeUp);
                }

                throw;
            }
        }

        public async Task<Guid> DeletePayload(InternalDriveFileId file, string key, Guid targetVersionTag, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            //Note: calling to get the file header, so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext);

            header.FileMetadata.VersionTag = targetVersionTag;

            var descriptorIndex = header.FileMetadata.Payloads?.FindIndex(p => p.KeyEquals(key)) ?? -1;

            if (descriptorIndex == -1)
            {
                return Guid.Empty;
            }

            var descriptor = header.FileMetadata.Payloads![descriptorIndex];


            header.FileMetadata.Payloads!.RemoveAt(descriptorIndex);
            await UpdateActiveFileHeader(file, header, odinContext);

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            // Delete the payload from disk AFTER we update the header successfully
            await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, file.FileId, [descriptor]);

            return header.FileMetadata.VersionTag.GetValueOrDefault(); // this works because we pass header all the way
        }

        public async Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            return await CreateServerHeaderInternal(file, keyHeader, metadata, serverMetadata, odinContext);
        }

        public async Task<EncryptedKeyHeader> EncryptKeyHeader(Guid driveId, KeyHeader keyHeader, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(driveId, odinContext);
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(driveId);

            (await this.DriveManager.GetDriveAsync(driveId)).AssertValidStorageKey(storageKey);

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        public async Task<bool> CallerHasPermissionToFile(ServerFileHeader header, IOdinContext odinContext)
        {
            if (null == header)
            {
                _logger.LogDebug($"Permission check called on null header");
                return false;
            }

            await AssertDriveIsNotArchived(header.FileMetadata.File.DriveId, odinContext);
            return await driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadDriveAsync(file.DriveId, odinContext);
            var header = await GetServerFileHeaderInternal(file, odinContext);

            if (header == null)
            {
                return null;
            }

            AssertValidFileSystemType(header.ServerMetadata);
            return header;
        }

        public async Task<ServerFileHeader> GetServerFileHeaderForWriting(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderInternal(file, odinContext);

            if (header == null)
            {
                return null;
            }

            AssertValidFileSystemType(header.ServerMetadata);
            return header;
        }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> of the target file and only enforces the Read
        /// permission; allowing you to determine the file system type when you don't have it.
        /// </summary>
        public async Task<FileSystemType> ResolveFileSystemType(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);

            var header = await GetServerFileHeaderInternal(file, odinContext);
            if (header == null)
            {
                throw new OdinSystemException($"Failed to resolve file system type, header does not exist for file id {file}");
            }

            return header.ServerMetadata.FileSystemType;
        }

        /// <param name="startExpiryClock">
        /// Whether this read counts as "someone opened it" for an expire-after-first-read Ttl. Opt-in,
        /// and only the caller-facing payload endpoint passes true. Plenty of internal callers stream a
        /// payload for their own reasons - packaging it for a peer transfer, publishing static content,
        /// reading a contact photo - and none of those are a reader opening the file. Defaulting this
        /// to false means a new internal caller cannot silently start burning files.
        /// </param>
        public async Task<PayloadStream> GetPayloadStreamAsync(InternalDriveFileId file, string key, FileChunk chunk,
            IOdinContext odinContext, bool startExpiryClock = false)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadDriveAsync(file.DriveId, odinContext);
            TenantPathManager.AssertValidPayloadKey(key);

            //Note: calling to get the file header will also
            //ensure the caller can touch this file.
            var header = await GetServerFileHeader(file, odinContext);
            if (header == null)
            {
                return null;
            }

            var descriptor = header.FileMetadata.GetPayloadDescriptor(key);

            if (descriptor == null)
            {
                return null;
            }

            if (startExpiryClock)
            {
                await TryResolveTtlOnFirstReadAsync(header, odinContext);
            }

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            try
            {
                var stream = await longTermStorageManager.GetPayloadStreamAsync(drive, file.FileId, descriptor, chunk);
                return new PayloadStream(descriptor, stream.Length, stream);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<bool> FileExists(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            return await longTermStorageManager.HeaderFileExists(drive, file.FileId, GetFileSystemType());
        }

        public async Task<bool> SoftDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);

            var existingHeader = await this.GetServerFileHeaderInternal(file, odinContext, includeExpired: true);

            return await WriteDeletedFileHeader(existingHeader, odinContext, markComplete);
        }

        public async Task HardDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var fileHeader = await GetServerFileHeaderInternal(file, odinContext, includeExpired: true);

            if (fileHeader == null)
            {
                return;
            }

            await longTermStorageManager.HardDeleteAsync(drive, file.FileId, fileHeader.FileMetadata.Payloads);

            if (await TryShouldRaiseDriveEventAsync(file))
            {
                await TryPublishAsync(new DriveFileDeletedNotification
                {
                    IsHardDelete = true,
                    File = file,
                    ServerFileHeader = null,
                    SharedSecretEncryptedFileHeader = null,
                    OdinContext = odinContext,
                });
            }
        }

        public async Task<(bool success, List<PayloadDescriptor> payloads)> CommitNewFile(
            InternalDriveFileId originFile, KeyHeader keyHeader,
            FileMetadata newMetadata, ServerMetadata serverMetadata,
            bool? ignorePayload, IOdinContext odinContext, StagingArea sourceArea, Guid? useThisVersionTag = null, WriteSecondDatabaseRowBase markComplete = null)
        {
            await AssertDriveIsNotArchived(originFile.DriveId, odinContext);
            await AssertCanWriteToDrive(originFile.DriveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(originFile.DriveId);

            ignorePayload = ignorePayload.GetValueOrDefault(false) || newMetadata.PayloadsAreRemote;

            var targetFile = originFile;
            newMetadata.File = targetFile; // this is a new file so we can use the same fileId from the incoming file
            serverMetadata.FileSystemType = GetFileSystemType();

            // First copy and prepare everything we need
            if (!ignorePayload.GetValueOrDefault(false))
            {
                await CopyPayloadsAndThumbnailsToLongTermStorage(originFile, targetFile, newMetadata.Payloads ?? [], drive, sourceArea);
            }

            // set the version tag null on a new file sine it will be handled by the
            // db and this stops conflicts if someone passes in useThisVersionTag 
            newMetadata.VersionTag = null;

            var serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, newMetadata, serverMetadata, odinContext);
            bool success = false;

            try
            {
                await AssertPayloadsExistOnFileSystemAsync(serverHeader);

                await using (var tx = await db.BeginStackedTransactionAsync())
                {
                    // Now commit the file header to the database and the inbox record in one transaction
                    await WriteNewFileHeader(targetFile, serverHeader, odinContext, useThisVersionTag: useThisVersionTag);

                    _logger.LogDebug("{method} -> markComplete {message}",
                        nameof(CommitNewFile),
                        markComplete == null ? "is not configured" : "will be called");

                    if (markComplete != null)
                    {
                        int n = await markComplete.ExecuteAsync();
                        if (n != 1)
                            throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
                    }

                    tx.Commit();
                    success = true;
                }

                await TryScheduleExpiryAsync(serverHeader, odinContext);

                if (serverHeader != null && await TryShouldRaiseDriveEventAsync(targetFile))
                {
                    await TryPublishAsync(new DriveFileAddedNotification
                    {
                        File = originFile,
                        ServerFileHeader = serverHeader,
                        OdinContext = odinContext,
                    });
                }
            }
            finally
            {
                if (!success && !ignorePayload.GetValueOrDefault())
                {
                    // clean up the newly copied payloads since we failed to update the header 
                    await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId, newMetadata.Payloads);
                }
            }

            return (success, newMetadata.Payloads);
        }

        public async Task<(bool success, List<PayloadDescriptor> payloads)> OverwriteFile(
            InternalDriveFileId originFile, InternalDriveFileId targetFile,
            KeyHeader keyHeader, FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload, IOdinContext odinContext, WriteSecondDatabaseRowBase markComplete,
            StagingArea sourceArea)
        {
            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
            ignorePayload = ignorePayload.GetValueOrDefault(false) || newMetadata.PayloadsAreRemote;

            List<PayloadDescriptor> payloads = [];
            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Cannot overwrite file that does not exist", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            newMetadata.TransitCreated = existingServerHeader.FileMetadata.TransitCreated;
            newMetadata.TransitUpdated = existingServerHeader.FileMetadata.TransitUpdated;
            newMetadata.OriginalAuthor = existingServerHeader.FileMetadata.OriginalAuthor;
            newMetadata.SenderOdinId = existingServerHeader.FileMetadata.SenderOdinId;
            newMetadata.SetCreatedModifiedWithDatabaseValue(existingServerHeader.FileMetadata.Created,
                existingServerHeader.FileMetadata.Updated);

            //Only overwrite the globalTransitId if one is already set; otherwise let a file update set the ID (useful for mail-app drafts)
            if (existingServerHeader.FileMetadata.GlobalTransitId != null)
            {
                newMetadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            }

            newMetadata.FileState = existingServerHeader.FileMetadata.FileState;
            newMetadata.ReactionPreview = existingServerHeader.FileMetadata.ReactionPreview;
            ApplyShortenOnlyTtl(existingServerHeader.FileMetadata, newMetadata);

            newMetadata.File = existingServerHeader.FileMetadata.File;
            //Note: our call to GetServerFileHeader earlier validates the existing
            serverMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            payloads = newMetadata.Payloads ?? [];

            // Snapshot what the committed header points at before anything is copied over it. Both the mid-copy
            // rollback and the post-commit-failure rollback below must leave these objects alone.
            var committedPayloads = existingServerHeader.FileMetadata.Payloads?.ToList() ?? [];

            if (!ignorePayload.GetValueOrDefault(false))
            {
                await CopyPayloadsAndThumbnailsToLongTermStorage(originFile, targetFile, payloads, drive, sourceArea,
                    committedPayloads);
            }

            bool success = false;

            var serverHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = await EncryptKeyHeader(originFile.DriveId, keyHeader, odinContext),
                FileMetadata = newMetadata,
                ServerMetadata = serverMetadata
            };

            try
            {
                await AssertPayloadsExistOnFileSystemAsync(serverHeader);
                await using (var tx = await db.BeginStackedTransactionAsync())
                {
                    // Now commit the file header to the database and the inbox record in one transaction
                    await WriteFileHeaderInternal(serverHeader, odinContext);
                    _logger.LogDebug("{method} -> markComplete {message}",
                        nameof(OverwriteFile),
                        markComplete == null ? "is not configured" : "will be called");

                    if (markComplete != null)
                    {
                        int n = await markComplete.ExecuteAsync();
                        if (n != 1)
                            throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
                    }

                    tx.Commit();
                    success = true;
                }

                // The TTL may have been shortened by this write; queue a delete for the new time. The
                // job already queued for the old time re-reads the header and no-ops if it lost the race.
                await TryScheduleExpiryAsync(serverHeader, odinContext);

                if (serverHeader != null && await TryShouldRaiseDriveEventAsync(targetFile))
                {
                    await TryPublishAsync(new DriveFileChangedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = serverHeader,
                        OdinContext = odinContext,
                    });
                }
            }
            finally
            {
                if (success)
                {
                    if (!ignorePayload.GetValueOrDefault(false))
                    {
                        //Since this method is a full overwrite, zombies are all payloads on the file being
                        //overwritten, except any the incoming header reuses in place: a peer retransmit carries the
                        //sender's original descriptor, so an incoming payload can share Key and Uid (and therefore
                        //its stored object) with the one it replaces. Deleting that would destroy live bytes.
                        var zombiePayloads = PayloadStorage.ExcludeStillLive(committedPayloads, payloads);
                        await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId, zombiePayloads);
                    }
                }
                else
                {
                    if (!ignorePayload.GetValueOrDefault(false))
                    {
                        // The header was never replaced, so the old one still references committedPayloads. Roll back
                        // only the incoming copies that live at their own objects.
                        await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId,
                            PayloadStorage.ExcludeStillLive(payloads, committedPayloads));
                    }
                }
            }

            return (success, payloads);
        }

        public async Task<Guid> UpdatePayloads(
            InternalDriveFileId originFile,
            InternalDriveFileId targetFile,
            List<PayloadDescriptor> incomingPayloads,
            IOdinContext odinContext,
            Guid expectedVersionTag)
        {
            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext);

            if (null == existingServerHeader)
            {
                throw new OdinClientException("Invalid target file", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            // Snapshot the committed payload set: existingServerHeader is mutated in place further down, and both
            // cleanup paths below have to reason about what the DB header referenced before that.
            var committedPayloads = existingServerHeader.FileMetadata.Payloads?.ToList() ?? [];

            // zombies will be those payloads that we overwrite, minus any the incoming set reuses in place
            // (same Key and Uid resolves to the same stored object, so deleting it would destroy live bytes)
            var zombiePayloads = PayloadStorage.ExcludeStillLive(
                committedPayloads
                    .Where(existingPayload => incomingPayloads.Any(incomingPayload => incomingPayload.KeyEquals(existingPayload))),
                incomingPayloads);

            try
            {
                //Note: we do not delete existing payloads.  this feature adds or overwrites existing ones
                await CopyPayloadsAndThumbnailsToLongTermStorage(originFile, targetFile, incomingPayloads, drive,
                    StagingArea.Upload, committedPayloads);

                // get all the existing payloads that are not in the incoming list, we'll keep these
                var payloadsToKeep = existingServerHeader.FileMetadata.Payloads.Where(
                    existingPayload => incomingPayloads.All(incomingPayload => !incomingPayload.KeyEquals(existingPayload)));

                var allPayloads = incomingPayloads.Concat(payloadsToKeep).ToList();

                existingServerHeader.FileMetadata.Payloads = allPayloads;
                existingServerHeader.FileMetadata.VersionTag = expectedVersionTag;

                await WriteFileHeaderInternal(existingServerHeader, odinContext);
            }
            catch (Exception)
            {
                // The header write failed, so the committed header still references committedPayloads; only the
                // incoming copies that landed on their own objects are safe to reclaim.
                await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId,
                    PayloadStorage.ExcludeStillLive(incomingPayloads, committedPayloads));
                throw;
            }

            try
            {
                if (await TryShouldRaiseDriveEventAsync(targetFile))
                {
                    await TryPublishAsync(new DriveFileChangedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = existingServerHeader,
                        OdinContext = odinContext,
                    });
                }
            }
            finally
            {
                // Remove the replaced payloads (only if DB successful)
                await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId, zombiePayloads);
            }

            return existingServerHeader.FileMetadata.VersionTag.GetValueOrDefault();
        }

        public async Task OverwriteMetadata(byte[] newKeyHeaderIv, InternalDriveFileId targetFile, FileMetadata newMetadata,
            ServerMetadata newServerMetadata,
            IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext);

            await OverwriteMetadataInternal(newKeyHeaderIv, existingServerHeader, newMetadata, newServerMetadata, odinContext);

            if (await TryShouldRaiseDriveEventAsync(targetFile))
            {
                await TryPublishAsync(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                });
            }
        }

        public async Task UpdateReactionSummary(InternalDriveFileId targetFile, ReactionSummary summary, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            odinContext.PermissionsContext.AssertHasAtLeastOneDrivePermission(targetFile.DriveId,
                DrivePermission.React,
                DrivePermission.Comment,
                DrivePermission.Write);

            summary ??= new ReactionSummary();

            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
            var existingHeader = await longTermStorageManager.GetServerFileHeader(drive, targetFile.FileId, GetFileSystemType());
            existingHeader.FileMetadata.ReactionPreview = summary;

            await longTermStorageManager.SaveReactionHistory(drive, targetFile.FileId, summary);

            if (await TryShouldRaiseDriveEventAsync(targetFile))
            {
                // Re-read so the notification carries a snapshot consistent with what was just
                // persisted: the bumped `modified`/updated timestamp and the current localReactions
                // (the group reaction flow updates localReactions before this runs). Publishing the
                // pre-write `existingHeader` previously emitted a stale statisticsChanged (old
                // `updated` and missing the reaction it announces).
                var refreshedHeader =
                    await longTermStorageManager.GetServerFileHeader(drive, targetFile.FileId, GetFileSystemType())
                    ?? existingHeader;

                await TryPublishAsync(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = refreshedHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(refreshedHeader, odinContext),
                    OdinContext = odinContext,
                });
            }
        }

        public async Task InitiateTransferHistoryAsync(InternalDriveFileId file, OdinId recipient, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);

            //
            // Get and validate the header
            //
            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            var header = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());
            AssertValidFileSystemType(header.ServerMetadata);

            var (updatedHistory, modifiedTime) =
                await longTermStorageManager.InitiateTransferHistoryAsync(drive.Id, file.FileId, recipient);

            // note: I'm just avoiding re-reading the file.
            header.ServerMetadata.TransferHistory = updatedHistory;
            header.FileMetadata.SetCreatedModifiedWithDatabaseValue(header.FileMetadata.Created, modifiedTime);

            if (await TryShouldRaiseDriveEventAsync(file))
            {
                await TryPublishAsync(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = header,
                    OdinContext = odinContext,
                    IgnoreFeedDistribution = true,
                    IgnoreReactionPreviewCalculation = true
                });
            }
        }

        public async Task<bool> UpdateTransferHistory(InternalDriveFileId file, OdinId recipient, UpdateTransferHistoryData updateData,
            IOdinContext odinContext, WriteSecondDatabaseRowBase markComplete)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);

            bool success = false;

            try
            {
                //
                // Get and validate the header
                //
                var drive = await DriveManager.GetDriveAsync(file.DriveId);
                var header = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());
                AssertValidFileSystemType(header.ServerMetadata);

                _logger.LogDebug(
                    "Updating transfer history success on file:{file} for recipient:{recipient} Version:{versionTag}\t Status:{status}\t IsInOutbox:{outbox}\t ReadByRecipientTimestamp: {readTs}",
                    file,
                    recipient,
                    updateData.VersionTag,
                    updateData.LatestTransferStatus,
                    updateData.IsInOutbox,
                    updateData.ReadByRecipientTimestamp);

                await using (var tx = await db.BeginStackedTransactionAsync())
                {
                    var (updatedHistory, modifiedTime) = await longTermStorageManager.SaveTransferHistoryAsync(
                        drive.Id, file.FileId, recipient, updateData);

                    _logger.LogDebug("{method} -> markComplete {message}",
                        nameof(UpdateTransferHistory),
                        markComplete == null ? "is not configured" : "will be called");

                    if (markComplete != null)
                    {
                        int n = await markComplete.ExecuteAsync();
                        if (n != 1)
                            throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
                    }

                    tx.Commit();
                    success = true;

                    // note: I'm just avoiding re-reading the file.
                    header.ServerMetadata.TransferHistory = updatedHistory;
                    header.FileMetadata.SetCreatedModifiedWithDatabaseValue(header.FileMetadata.Created, modifiedTime);
                }


                if (await TryShouldRaiseDriveEventAsync(file))
                {
                    await TryPublishAsync(new DriveFileChangedNotification
                    {
                        File = file,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        IgnoreFeedDistribution = true,
                        IgnoreReactionPreviewCalculation = true
                    });
                }
            }
            catch (OdinSecurityException)
            {
                throw;
            }
            catch (OdinClientException)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured");
            }

            return success;
        }


        public async Task UpdateActiveFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext,
            bool raiseEvent = false)
        {
            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await UpdateActiveFileHeaderInternal(targetFile, header, odinContext, raiseEvent);
        }


        public async Task<(bool success, List<PayloadDescriptor> uploadedPayloads)> UpdateBatchAsync(InternalDriveFileId originFile,
            InternalDriveFileId targetFile,
            BatchUpdateManifest manifest,
            IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete,
            StagingArea sourceArea)
        {
            bool success = false;

            await AssertDriveIsNotArchived(targetFile.DriveId, odinContext);
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext);
            if (existingHeader.FileMetadata.DataSource != manifest.FileMetadata.DataSource)
            {
                throw new OdinClientException("Cannot change RemotePayloadIdentity on file updates",
                    OdinClientErrorCode.CannotModifyRemotePayloadIdentity);
            }

            // First prepare by copying everything needed
            var (header, copiedPayloads, zombies, committedPayloads) = await UpdateBatchCopyFilesAsync(originFile, targetFile,
                manifest, odinContext, sourceArea);
            try
            {
                await AssertPayloadsExistOnFileSystemAsync(header);
                await using (var tx = await db.BeginStackedTransactionAsync())
                {
                    // Now commit the file header to the database and the inbox record in one transaction
                    await OverwriteMetadataInternal(manifest.KeyHeader.Iv, header, manifest.FileMetadata,
                        manifest.ServerMetadata, odinContext, manifest.NewVersionTag);

                    _logger.LogDebug("{method} -> markComplete {message}",
                        nameof(UpdateBatchAsync),
                        markComplete == null ? "is not configured" : "will be called");

                    if (markComplete != null)
                    {
                        int n = await markComplete.ExecuteAsync();
                        if (n != 1)
                            throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
                    }

                    tx.Commit();
                    success = true;
                    // After success has been set to true we really should return TRUE.
                    // If an exception occurs it will be caught in the exception handler of
                    // e.g. the Inbox, which will then likely mark the inbox item for "try again",
                    // which will then fail because the inbox item is gone. Which is not terrible,
                    // but it's better not to get in that situation :-) 
                }

                // And publish the event
                if (await TryShouldRaiseDriveEventAsync(targetFile))
                {
                    await TryPublishAsync(new DriveFileChangedNotification // TODO remove await
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        IgnoreFeedDistribution = false
                    });
                }
            }
            finally
            {
                if (success)
                {
                    // Cleanup zombied payloads only if the file got moved and 
                    var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
                    await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId, zombies);
                }
                else
                {
                    // clean up the newly copied payloads since we failed to update the header; the committed header is
                    // unchanged, so anything sharing a stored object with it must be left alone
                    var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
                    await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId,
                        PayloadStorage.ExcludeStillLive(copiedPayloads, committedPayloads));
                }
            }

            // No need to return success, if we are here, it is true
            return (success, copiedPayloads);
        }


        private async Task TryPublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            try
            {
                await mediator.Publish(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to Publish() notification");
            }
        }


        private async Task<(ServerFileHeader success, List<PayloadDescriptor> copiedPayloads, List<PayloadDescriptor> zombies,
                List<PayloadDescriptor> committedPayloads)>
            UpdateBatchCopyFilesAsync(InternalDriveFileId originFile,
                InternalDriveFileId targetFile, BatchUpdateManifest manifest,
                IOdinContext odinContext, StagingArea sourceArea)
        {
            List<PayloadDescriptor> copiedPayloads = new();
            List<PayloadDescriptor> committedPayloads = [];

            List<PayloadDescriptor> DeleteFileReferencesFromHeader(ServerFileHeader existingHeader1)
            {
                var zombies = new List<PayloadDescriptor>();

                var instructions = manifest.PayloadInstruction ?? [];

                // Delete operations are for existing payloads, so we need to update the existing header
                foreach (var op in instructions.Where(op => op.OperationType == PayloadUpdateOperationType.DeletePayload))
                {
                    var descriptor = existingHeader1.FileMetadata.GetPayloadDescriptor(op.Key);
                    if (descriptor != null)
                    {
                        existingHeader1.FileMetadata.Payloads.RemoveAll(pk => pk.KeyEquals(op.Key));
                        zombies.Add(descriptor);
                    }
                }

                return zombies;
            }

            async Task<List<PayloadDescriptor>> ProcessAppendOrOverwrite(StorageDrive storageDrive, ServerFileHeader serverFileHeader)
            {
                var zombies = new List<PayloadDescriptor>();

                var instructions = manifest.PayloadInstruction ?? [];
                foreach (var op in instructions.Where(op => op.OperationType == PayloadUpdateOperationType.AppendOrOverwrite))
                {
                    // Here look at the incoming payloads because we're adding a new one or overwriting
                    var descriptor = manifest.FileMetadata.GetPayloadDescriptor(op.Key, true, $"Could not find payload " +
                        $"with key {op.Key} in FileMetadata to " +
                        $"perform operation {op.OperationType}");

                    // keep a list of payloads that would have been uploaded for the append or overwrite operation
                    copiedPayloads.Add(descriptor);

                    //
                    // Upsert the descriptor in the existing header
                    //
                    var idx = serverFileHeader.FileMetadata.Payloads.FindIndex(p => p.KeyEquals(op.Key));

                    if (idx == -1)
                    {
                        // item was new (appended)
                        serverFileHeader.FileMetadata.Payloads.Add(descriptor);
                    }
                    else
                    {
                        // payload was overwritten so this means we need to
                        // mark the old one as a zombie
                        zombies.Add(serverFileHeader.FileMetadata.Payloads[idx]);
                        serverFileHeader.FileMetadata.Payloads[idx] = descriptor;
                    }
                }

                // Copy all payload from the temp folder to the long term folder
                await CopyPayloadsAndThumbnailsToLongTermStorage(originFile, targetFile, copiedPayloads, storageDrive, sourceArea,
                    committedPayloads);

                return zombies;
            }

            OdinValidationUtils.AssertNotEmptyGuid(manifest.NewVersionTag, nameof(manifest.NewVersionTag));
            var metadata = manifest.FileMetadata;
            metadata?.Validate(odinContext.Tenant);
            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext);

            //
            // Validations
            //
            if (null == existingHeader)
            {
                throw new OdinClientException("File being updated does not exist", OdinClientErrorCode.InvalidFile);
            }

            if (existingHeader.FileMetadata.IsEncrypted)
            {
                var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(existingHeader.FileMetadata.File.DriveId);
                var existingKeyHeader = existingHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

                if (!ByteArrayUtil.EquiByteArrayCompare(manifest.KeyHeader.AesKey.GetKey(), existingKeyHeader.AesKey.GetKey()))
                {
                    throw new OdinClientException("When updating an encrypted file, the AES key must match the existing key. " +
                                                  "Changing the AES key can invalidate existing encrypted payloads.  " +
                                                  "If you need to rotate keys, re-upload the file instead.",
                        OdinClientErrorCode.InvalidKeyHeader);
                }

                if (ByteArrayUtil.EquiByteArrayCompare(manifest.KeyHeader.Iv, existingKeyHeader.Iv))
                {
                    throw new OdinClientException("When updating a file, you must change the Iv",
                        OdinClientErrorCode.MustRotateKeyHeaderIvWhenUpdating);
                }
            }

            //
            // For the payloads, we have two sources and one set of operations
            // 1. manifest.FileMetadata.Payloads - indicates the payloads uploaded by the client for this batch update
            // 2. existingHeader.FileMetadata.Payloads - indicates the payload descriptors in current state on the server
            // 3. manifest.PayloadInstruction - this indicates what to do with the payloads on the file

            // now - each PayloadInstruction needs to be applied
            // to delete a payload, remove from disk and remove from the existingHeader.FileMetadata.Payloads
            // to add or append a payload, save on disk then upsert the value in existingHeader.FileMetadata.Payloads
            // 
            // Note: I have separated the payload instructions for readability
            // 

            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
            var zombies = new List<PayloadDescriptor>();

            // ProcessAppendOrOverwrite upserts descriptors into existingHeader before copying any bytes, so snapshot
            // what the committed header referenced first. Both the mid-copy rollback and the caller's
            // failure branch need it to tell a genuine orphan from a live object.
            committedPayloads = existingHeader.FileMetadata.Payloads?.ToList() ?? [];

            zombies.AddRange(await ProcessAppendOrOverwrite(drive, existingHeader));
            zombies.AddRange(DeleteFileReferencesFromHeader(existingHeader));

            // At this point we have now copied all the files successfully and return
            // the payloads that must be cleaned up. The caller can now do its DB
            // stuff and then call cleanup.
            // Never hand back a zombie that shares its stored object (Key + Uid) with a payload the resulting
            // header still points at; deleting it would destroy live bytes rather than a previous version.
            var deletableZombies = PayloadStorage.ExcludeStillLive(zombies, existingHeader.FileMetadata.Payloads);

            return (existingHeader, copiedPayloads, deletableZombies, committedPayloads);
        }


        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataTags(InternalDriveFileId file,
            Guid targetVersionTag,
            List<Guid> newTags,
            IOdinContext odinContext)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");
            OdinValidationUtils.AssertIsTrue(newTags.Count <= 50, "max local tags is 50");

            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderForWriting(file, odinContext);
            if (null == header)
            {
                throw new OdinClientException("Cannot update local app data for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            var newVersionTag = DriveFileUtility.CreateVersionTag();

            var mergedMetadata = new LocalAppMetadata
            {
                Iv = header.FileMetadata.LocalAppData?.Iv,
                VersionTag = targetVersionTag,
                Content = header.FileMetadata.LocalAppData?.Content,
                Tags = newTags,
                ReadTime = header.FileMetadata.LocalAppData?.ReadTime,
                LocalReactions = header.FileMetadata.LocalAppData?.LocalReactions,
            };

            await longTermStorageManager.SaveLocalMetadataTagsAsync(file, mergedMetadata, newVersionTag);

            var updatedHeader = await GetServerFileHeaderForWriting(file, odinContext);
            if (await TryShouldRaiseDriveEventAsync(file))
            {
                await TryPublishAsync(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = updatedHeader,
                    OdinContext = odinContext,
                });
            }

            return new UpdateLocalMetadataResult()
            {
                NewLocalVersionTag = newVersionTag
            };
        }

        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataContent(InternalDriveFileId file, Guid targetVersionTag,
            byte[] initVector,
            string newContent,
            IOdinContext odinContext)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");
            // DriveFileUtility.AssertValidLocalAppContentLength(newContent); REPLACED WITH mergedMetadata.Validate();

            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderForWriting(file, odinContext);
            if (null == header)
            {
                throw new OdinClientException("Cannot update local app data for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            if (header.FileMetadata.IsEncrypted && !ByteArrayUtil.IsStrongKey(initVector))
            {
                throw new OdinClientException("A string IV is required when the target file is encrypted");
            }

            var newVersionTag = DriveFileUtility.CreateVersionTag();

            var mergedMetadata = new LocalAppMetadata
            {
                VersionTag = targetVersionTag,
                Iv = initVector,
                Content = newContent,
                Tags = header.FileMetadata.LocalAppData?.Tags ?? [],
                ReadTime = header.FileMetadata.LocalAppData?.ReadTime,
                LocalReactions = header.FileMetadata.LocalAppData?.LocalReactions,
            };

            mergedMetadata.Validate();

            await longTermStorageManager.SaveLocalMetadataAsync(file, mergedMetadata, newVersionTag);

            var updatedHeader = await GetServerFileHeaderForWriting(file, odinContext);
            if (await TryShouldRaiseDriveEventAsync(file))
            {
                await TryPublishAsync(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = updatedHeader,
                    OdinContext = odinContext,
                });
            }

            return new UpdateLocalMetadataResult()
            {
                NewLocalVersionTag = newVersionTag
            };
        }

        public async Task<bool> UpdateLocalReadTime(InternalDriveFileId file, IOdinContext odinContext, UnixTimeUtc? timestamp = null)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");

            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderForWriting(file, odinContext);
            if (null == header)
            {
                throw new OdinClientException("Cannot update local app data for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            var existingLocalAppData = header.FileMetadata.LocalAppData;

            // If no timestamp provided and ReadTime is already set, no update needed
            if (!timestamp.HasValue && existingLocalAppData?.ReadTime != null)
            {
                return false;
            }

            var now = UnixTimeUtc.Now();
            var effectiveReadTime = timestamp.HasValue
                ? (timestamp.Value < now ? timestamp.Value : now)
                : now;

            if (existingLocalAppData?.ReadTime != null && existingLocalAppData.ReadTime.Value >= effectiveReadTime)
            {
                return false;
            }

            var newVersionTag = DriveFileUtility.CreateVersionTag();
            var mergedMetadata = new LocalAppMetadata
            {
                VersionTag = existingLocalAppData?.VersionTag ?? Guid.Empty,
                Iv = existingLocalAppData?.Iv,
                Content = existingLocalAppData?.Content,
                Tags = existingLocalAppData?.Tags ?? [],
                ReadTime = effectiveReadTime,
                LocalReactions = existingLocalAppData?.LocalReactions,
            };

            mergedMetadata.Validate();

            await longTermStorageManager.SaveLocalMetadataAsync(file, mergedMetadata, newVersionTag);

            try
            {
                var updatedHeader = await GetServerFileHeaderForWriting(file, odinContext);
                if (await TryShouldRaiseDriveEventAsync(file))
                {
                    await TryPublishAsync(new DriveFileChangedNotification
                    {
                        File = file,
                        ServerFileHeader = updatedHeader,
                        OdinContext = odinContext,
                    });
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to send DriveFileChangedNotification");
            }

            return true;
        }

        public async Task UpdateLocalReactionsAsync(InternalDriveFileId file, List<string> localReactions,
            IOdinContext odinContext)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");

            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderForWriting(file, odinContext);
            if (null == header)
            {
                return;
            }

            var existingLocalAppData = header.FileMetadata.LocalAppData;

            var newVersionTag = DriveFileUtility.CreateVersionTag();
            var mergedMetadata = new LocalAppMetadata
            {
                VersionTag = existingLocalAppData?.VersionTag ?? Guid.Empty,
                Iv = existingLocalAppData?.Iv,
                Content = existingLocalAppData?.Content,
                Tags = existingLocalAppData?.Tags ?? [],
                ReadTime = existingLocalAppData?.ReadTime,
                LocalReactions = localReactions,
            };

            mergedMetadata.Validate();

            await longTermStorageManager.SaveLocalMetadataAsync(file, mergedMetadata, newVersionTag);

            try
            {
                var updatedHeader = await GetServerFileHeaderForWriting(file, odinContext);
                if (await TryShouldRaiseDriveEventAsync(file))
                {
                    await TryPublishAsync(new DriveFileChangedNotification
                    {
                        File = file,
                        ServerFileHeader = updatedHeader,
                        OdinContext = odinContext,
                        // localReactions is only written in the group-reaction flow, which also raises
                        // a ReactionPreviewUpdatedNotification (now a fileModified) carrying the full
                        // fresh header. Suppress this redundant WS broadcast so a reaction sends the
                        // header once; the MediatR event still fires for cache/feed handlers.
                        IgnoreWebSocketNotification = true,
                    });
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to send DriveFileChangedNotification for local reactions update");
            }
        }

        public async Task CleanupUploadTemporaryFiles(InternalDriveFileId file, List<PayloadDescriptor> descriptors,
            IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            if (await CanWriteToDrive(file.DriveId, odinContext))
            {
                await uploadStorageManager.CleanupUploadFiles(file, descriptors);
            }
        }

        // TODO:INBOX Both producers are migrated (peer-transfer and feed both carry metadata on the inbox row),
        // so nothing new is written to the folder. Delete once the folder is drained: no pre-upgrade items remain
        // in any inbox (every TransferInboxItem carries a non-null FileMetadata, so
        // PeerInboxProcessor.ResolveInboxSourceArea never returns StagingArea.Inbox). At that point the whole
        // legacy inbox-folder path is dead and can go together: StagingArea.Inbox, WriteInboxStream,
        // InboxStorageManager, InboxFileStore, the inbox path helpers, and this wrapper.
        public async Task CleanupInboxTemporaryFiles(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertDriveIsNotArchived(file.DriveId, odinContext);
            if (await CanWriteToDrive(file.DriveId, odinContext))
            {
                await inboxStorageManager.CleanupInboxFiles(file);
            }
        }

        /// <summary>
        /// Deletes long-term payload/thumbnail files that the peer receive path streamed directly under the
        /// incoming fileId for an inbox item that is now being abandoned (failed permanently) without ever
        /// committing a header. This prevents leaks introduced by writing inbox-routed payloads straight to
        /// long-term storage instead of the inbox folder.
        /// </summary>
        /// <remarks>
        /// Only call this on the give-up path. A successfully committed new file keeps the SAME (incoming) fileId, so
        /// deleting its payloads here would destroy live data; the success path must not invoke this.
        /// <paramref name="directlyWrittenPayloads"/> is the inbox row's payload descriptors; it is empty/null for
        /// legacy items whose payloads lived in the inbox folder, making this a no-op for them.
        /// </remarks>
        public async Task CleanupAbandonedLongTermPayloads(InternalDriveFileId incomingFile,
            List<PayloadDescriptor> directlyWrittenPayloads, IOdinContext odinContext)
        {
            if (directlyWrittenPayloads == null || directlyWrittenPayloads.Count == 0)
            {
                return;
            }

            await AssertDriveIsNotArchived(incomingFile.DriveId, odinContext);
            if (!await CanWriteToDrive(incomingFile.DriveId, odinContext))
            {
                return;
            }

            var drive = await DriveManager.GetDriveAsync(incomingFile.DriveId);

            // Only delete payloads still present. A failure that occurred during/after CommitNewFile/OverwriteFile
            // already removed them in those methods' cleanup; re-deleting would log spurious "does not exist"
            // errors. We only need to catch the pre-commit failures where the directly-written payloads remain.
            var stillPresent = new List<PayloadDescriptor>();
            foreach (var descriptor in directlyWrittenPayloads)
            {
                if (await longTermStorageManager.PayloadExistsOnDiskAsync(drive, incomingFile.FileId, descriptor))
                {
                    stillPresent.Add(descriptor);
                }
            }

            if (stillPresent.Count > 0)
            {
                await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, incomingFile.FileId, stillPresent);
            }
        }

        /// <summary>
        /// Sums the number of bytes spent
        /// for each payload, for each payload's thumbs,
        /// If payloads are remote the result is zero.
        /// </summary>
        /// <returns>Number of bytes used by payloads and thumbs (zero if remote)</returns>
        public static (long payloadBytes, long thumbBytes) PayloadByteCount(ServerFileHeader header)
        {
            long payloadDiskUsage = 0;
            long thumbnailDiskUsage = 0;

            if (!header.FileMetadata.PayloadsAreRemote)
            {
                payloadDiskUsage = header.FileMetadata.Payloads?.Sum(p => p.BytesWritten) ?? 0;

                thumbnailDiskUsage = header.FileMetadata.Payloads?
                    .SelectMany(p => p.Thumbnails ?? new List<ThumbnailDescriptor>())
                    .Sum(pp => pp.BytesWritten) ?? 0;
            }

            return (payloadDiskUsage, thumbnailDiskUsage);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header, IOdinContext odinContext, Guid? useThisVersionTag = null)
        {
            // Note: these validations here are just-in-case checks; however at this point many
            // other operations will have occured, so these checks also exist in the upload validation

            _logger.LogDebug("Calling header.validate on gtid: {file}", header.FileMetadata.GlobalTransitId);
            header.Validate(odinContext);

            var drive = await DriveManager.GetDriveAsync(header.FileMetadata.File.DriveId);

            // SaveFileHeader updates the Created / Updated fields in the header.FileMetadata
            await longTermStorageManager.SaveFileHeader(drive, header, useThisVersionTag);
        }

        /// <summary>
        /// Protects against using the wrong filesystem with a fileId
        /// </summary>
        /// <param name="serverMetadata"></param>
        /// <exception cref="OdinClientException"></exception>
        private void AssertValidFileSystemType(ServerMetadata serverMetadata)
        {
            if (serverMetadata.FileSystemType != GetFileSystemType())
            {
                //just in case the caller used the wrong drive service instance
                throw new OdinClientException(
                    $"Invalid SystemFileCategory.  This service only handles the FileSystemType of {GetFileSystemType()}");
            }
        }

        private Task<bool> ShouldRaiseDriveEventAsync(InternalDriveFileId file)
        {
            return Task.FromResult(file.DriveId != SystemDriveConstants.TransientTempDrive.Alias);
        }

        private async Task<bool> TryShouldRaiseDriveEventAsync(InternalDriveFileId file)
        {
            try
            {
                return await ShouldRaiseDriveEventAsync(file);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Publish Error occured");
                return false;
            }
        }

        private async Task<bool> WriteDeletedFileHeader(ServerFileHeader existingHeader, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            var file = existingHeader.FileMetadata.File;
            var payloadsToDelete = existingHeader.FileMetadata.Payloads ?? [];

            var metadata = existingHeader.FileMetadata;
            var appData = existingHeader.FileMetadata.AppData;
            var deletedServerFileHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = existingHeader.EncryptedKeyHeader,
                FileMetadata = new FileMetadata(existingHeader.FileMetadata.File)
                {
                    FileState = FileState.Deleted,
                    Created = metadata.Created,
                    Updated = UnixTimeUtc.Now().milliseconds,
                    TransitCreated = metadata.TransitCreated,
                    TransitUpdated = metadata.TransitUpdated,
                    SenderOdinId = metadata.SenderOdinId,
                    OriginalAuthor = metadata.OriginalAuthor,
                    GlobalTransitId = metadata.GlobalTransitId,
                    VersionTag = metadata.VersionTag,

                    AppData = new AppFileMetaData
                    {
                        UniqueId = null,
                        Tags = appData.Tags,
                        FileType = appData.FileType,
                        DataType = appData.DataType,
                        GroupId = appData.GroupId,
                        UserDate = appData.UserDate,
                        ArchivalStatus = appData.ArchivalStatus,

                        Content = "",
                        PreviewThumbnail = null,
                    },

                    ReferencedFile = null,
                    ReactionPreview = null,
                    IsEncrypted = false,
                    LocalAppData = null,
                    Payloads = null,
                    DataSource = null,
                },
                ServerMetadata = existingHeader.ServerMetadata
            };

            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            bool success = false;
            try
            {
                await using (var tx = await db.BeginStackedTransactionAsync())
                {
                    await WriteFileHeaderInternal(deletedServerFileHeader, odinContext);
                    await longTermStorageManager.DeleteReactionSummary(drive, deletedServerFileHeader.FileMetadata.File.FileId);
                    await longTermStorageManager.DeleteTransferHistoryAsync(drive, deletedServerFileHeader.FileMetadata.File.FileId);

                    _logger.LogDebug("{method} -> markComplete {message}",
                        nameof(WriteDeletedFileHeader),
                        markComplete == null ? "is not configured" : "will be called");

                    if (markComplete != null)
                    {
                        int n = await markComplete.ExecuteAsync();
                        if (n != 1)
                            throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
                    }

                    tx.Commit();
                }

                success = true;

                if (await TryShouldRaiseDriveEventAsync(file))
                {
                    await TryPublishAsync(new DriveFileDeletedNotification
                    {
                        PreviousServerFileHeader = existingHeader,
                        IsHardDelete = false,
                        File = file,
                        ServerFileHeader = deletedServerFileHeader,
                        SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(deletedServerFileHeader, odinContext),
                        OdinContext = odinContext,
                    });
                }
            }
            catch (OdinSecurityException)
            {
                throw;
            }
            catch (OdinClientException)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured");
            }
            finally
            {
                if (success)
                {
                    await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, file.FileId, payloadsToDelete);
                }
            }

            return success;
        }

        private async Task<ServerFileHeader> CreateServerHeaderInternal(InternalDriveFileId targetFile, KeyHeader keyHeader,
            FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext)
        {
            serverMetadata.FileSystemType = GetFileSystemType();

            return new ServerFileHeader()
            {
                EncryptedKeyHeader = metadata.IsEncrypted
                    ? await this.EncryptKeyHeader(targetFile.DriveId, keyHeader, odinContext)
                    : EncryptedKeyHeader.Empty(),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };
        }

        private async Task<ServerFileHeader> GetServerFileHeaderInternal(InternalDriveFileId file, IOdinContext odinContext,
            bool includeExpired = false)
        {
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var header = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());

            if (null == header)
            {
                return null;
            }

            // Belt and braces for the window between a TTL coming due and its job actually running.
            // Jobs lag; a file must never outlive its stated life just because the runner is busy.
            // The delete paths pass includeExpired so the expiry job can still see what it must remove.
            if (!includeExpired && IsExpired(header))
            {
                return null;
            }

            await driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);

            return header;
        }

        /// <summary>
        /// True when this file's <see cref="FileMetadata.Ttl"/> has come due. A pending
        /// (expire-after-first-read) TTL counts as expired once its unread backstop has passed.
        /// </summary>
        public static bool IsExpired(ServerFileHeader header)
        {
            var metadata = header.FileMetadata;
            var dueAt = FileTtl.ExpiresAt(metadata.Ttl, metadata.Created);
            return dueAt != null && dueAt.Value <= UnixTimeUtc.Now().milliseconds;
        }

        /// <summary>
        /// Queues the delete for a newly committed file that carries a TTL. Never allowed to fail the
        /// write: a file that got stored but whose job did not queue is recoverable, a write that
        /// rolled back because the scheduler hiccuped is not.
        /// </summary>
        private async Task TryScheduleExpiryAsync(ServerFileHeader header, IOdinContext odinContext)
        {
            if (header == null || FileTtl.IsNever(header.FileMetadata.Ttl))
            {
                return;
            }

            try
            {
                await fileExpiryScheduler.ScheduleExpiryAsync(header, odinContext);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to schedule expiry for file {file} on drive {drive}",
                    header.FileMetadata.File.FileId, header.FileMetadata.File.DriveId);
            }
        }

        /// <summary>
        /// Turns a pending (negative) TTL into a real point in time on this copy's first payload read,
        /// then queues the delete. This is the whole mechanism behind expire-after-reading, and it is
        /// deliberately hung off the payload read rather than the header read: mail clients and
        /// security scanners prefetch links, and the payload URL is not in the message.
        ///
        /// The write advances the version tag, because it must: <c>TableDriveMainIndex.cs:71</c>
        /// refuses an upsert that reuses the existing tag outright. That is arguably the honest
        /// outcome anyway - the file really did change state, and a client watching a burn countdown
        /// wants to see it. The cost is that a client holding the pre-read version tag will lose an
        /// optimistic-concurrency update it attempts afterwards.
        ///
        /// The re-read inside the transaction means a second concurrent reader finds the TTL already
        /// resolved and leaves it alone. Two readers racing at exactly the same instant could still
        /// both write; the values would differ by at most a few milliseconds, which is harmless.
        /// </summary>
        private async Task TryResolveTtlOnFirstReadAsync(ServerFileHeader header, IOdinContext odinContext)
        {
            if (!FileTtl.IsPendingFirstRead(header.FileMetadata.Ttl))
            {
                return;
            }

            var file = header.FileMetadata.File;

            try
            {
                var drive = await DriveManager.GetDriveAsync(file.DriveId);
                var resolved = FileTtl.ResolveFirstRead(header.FileMetadata.Ttl, UnixTimeUtc.Now());

                await using (var tx = await db.BeginStackedTransactionAsync())
                {
                    var fresh = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());
                    if (fresh == null || !FileTtl.IsPendingFirstRead(fresh.FileMetadata.Ttl))
                    {
                        return; // already read, already resolved by someone else
                    }

                    fresh.FileMetadata.Ttl = resolved;
                    await longTermStorageManager.SaveFileHeader(drive, fresh, useThisVersionTag: null);
                    tx.Commit();
                }

                header.FileMetadata.Ttl = resolved;

                await fileExpiryScheduler.ScheduleExpiryAtAsync(
                    file, header.ServerMetadata.FileSystemType, resolved, odinContext.Tenant);

                _logger.LogDebug("Resolved expire-after-read TTL for file {file} on drive {drive} to {resolved}",
                    file.FileId, file.DriveId, resolved);
            }
            catch (Exception e)
            {
                // A read must not fail because the clock could not be started.
                _logger.LogError(e, "Failed to resolve TTL on first read for file {file} on drive {drive}",
                    file.FileId, file.DriveId);
            }
        }

        /// <summary>
        /// Applies the shorten-only rule: an update may bring a file's death forward but never push it
        /// out. See <see cref="FileTtl.Shortest"/> for why this clamps instead of throwing.
        /// </summary>
        private void ApplyShortenOnlyTtl(FileMetadata existing, FileMetadata incoming)
        {
            var clamped = FileTtl.Shortest(incoming.Ttl, existing.Ttl, existing.Created);
            if (clamped != incoming.Ttl)
            {
                _logger.LogInformation(
                    "Update to file {file} tried to extend Ttl from {existingTtl} to {incomingTtl}; keeping the existing",
                    existing.File.FileId, existing.Ttl, incoming.Ttl);
            }

            incoming.Ttl = clamped;
        }

        /// <summary>
        /// Brings a file's death forward to now, at the request of a caller who can READ it. Safe to
        /// expose anonymously for exactly one reason: anyone who can make this call already holds the
        /// content, so the only thing they can take away is remaining lifetime - and only from a file
        /// that is already dying. A file with no Ttl is refused outright: hastening is not killing,
        /// and an anonymous visitor must never be able to destroy permanent public data.
        /// SECURITY DEBT: must additionally require the drive to be non-enumerable
        /// (BlockAnonymousEnumeration, not yet built) - see the banner on the expire-now endpoint.
        /// </summary>
        public async Task<bool> HastenExpiryAsync(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertCanReadDriveAsync(file.DriveId, odinContext);

            var header = await GetServerFileHeaderInternal(file, odinContext);
            if (header == null)
            {
                return false; // gone already, or never was - the caller cannot tell, on purpose
            }

            if (FileTtl.IsNever(header.FileMetadata.Ttl))
            {
                throw new OdinClientException("Only a file that already expires can be expired early",
                    OdinClientErrorCode.ArgumentError);
            }

            var nowMs = UnixTimeUtc.Now().milliseconds;
            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            await using (var tx = await db.BeginStackedTransactionAsync())
            {
                var fresh = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());
                if (fresh == null)
                {
                    return false;
                }

                // Shorten-only by construction: now is earlier than any live absolute Ttl, and a
                // pending negative Ttl resolves to at-least-now. Never extend, never resurrect.
                fresh.FileMetadata.Ttl = nowMs;
                await longTermStorageManager.SaveFileHeader(drive, fresh, useThisVersionTag: null);
                tx.Commit();
            }

            try
            {
                await fileExpiryScheduler.ScheduleExpiryAtAsync(
                    file, header.ServerMetadata.FileSystemType, nowMs, odinContext.Tenant);
            }
            catch (Exception e)
            {
                // The file is already dead - Ttl <= now refuses every read - the job only tombstones
                // it eagerly. Same best-effort stance as TryScheduleExpiryAsync.
                _logger.LogError(e, "Failed to schedule eager tombstoning of hastened file {file} on drive {drive}",
                    file.FileId, file.DriveId);
            }

            _logger.LogInformation("Expiry hastened to now for file {file} on drive {drive}",
                file.FileId, file.DriveId);

            return true;
        }

        /// <summary>
        /// Loads a header for the TTL jobs, which must be able to see a file precisely because it has
        /// expired. Every other read path hides expired files.
        /// </summary>
        public async Task<ServerFileHeader> GetServerFileHeaderForExpiry(InternalDriveFileId file, IOdinContext odinContext)
        {
            return await GetServerFileHeaderInternal(file, odinContext, includeExpired: true);
        }

        private async Task OverwriteMetadataInternal(byte[] keyHeaderIv, ServerFileHeader existingServerHeader, FileMetadata newMetadata,
            ServerMetadata newServerMetadata,
            IOdinContext odinContext, Guid? useThisVersionTag = null)
        {
            if (newMetadata.IsEncrypted && !ByteArrayUtil.IsStrongKey(keyHeaderIv))
            {
                throw new OdinClientException("KeyHeader Iv is not specified or is too weak");
            }

            if (null == existingServerHeader)
            {
                throw new OdinClientException("Cannot overwrite file that does not exist", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            if (existingServerHeader.FileMetadata.IsEncrypted != newMetadata.IsEncrypted)
            {
                throw new OdinClientException($"Cannot change encryption when storage intent is {StorageIntent.MetadataOnly} since your " +
                                              $"payloads might be invalidated", OdinClientErrorCode.ArgumentError);
            }

            var targetFile = existingServerHeader.FileMetadata.File;
            newMetadata.File = targetFile;
            newMetadata.SetCreatedModifiedWithDatabaseValue(existingServerHeader.FileMetadata.Created,
                existingServerHeader.FileMetadata.Updated);
            newMetadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            newMetadata.FileState = existingServerHeader.FileMetadata.FileState;
            newMetadata.Payloads = existingServerHeader.FileMetadata.Payloads;
            newMetadata.ReactionPreview = existingServerHeader.FileMetadata.ReactionPreview;
            newMetadata.OriginalAuthor = existingServerHeader.FileMetadata.OriginalAuthor;
            newMetadata.SenderOdinId = existingServerHeader.FileMetadata.SenderOdinId;
            ApplyShortenOnlyTtl(existingServerHeader.FileMetadata, newMetadata);

            //fields we keep
            newServerMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;
            newServerMetadata.OriginalRecipientCount = existingServerHeader.ServerMetadata.OriginalRecipientCount;

            //only change the IV if the file was encrypted
            if (existingServerHeader.FileMetadata.IsEncrypted)
            {
                // Critical Note: if this new key header's AES key does not match the
                // payload's encryption; the data is lost forever.  (for-ev-er, capish?)
                var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(targetFile.DriveId);
                var existingDecryptedKeyHeader = existingServerHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
                var newKeyHeader = new KeyHeader()
                {
                    Iv = keyHeaderIv,
                    AesKey = existingDecryptedKeyHeader.AesKey
                };

                existingServerHeader.EncryptedKeyHeader = await this.EncryptKeyHeader(targetFile.DriveId, newKeyHeader, odinContext);
            }

            existingServerHeader.FileMetadata = newMetadata;
            existingServerHeader.ServerMetadata = newServerMetadata;
            await WriteFileHeaderInternal(existingServerHeader, odinContext, useThisVersionTag); // Sets header.FileMetadata.Created/Updated
            await TryScheduleExpiryAsync(existingServerHeader, odinContext);
        }

        /// <summary>
        /// Copies payloads and thumbs to long term storage
        /// </summary>
        /// <returns>List of all files copied (directory and filename)</returns>
        /// <param name="committedPayloads">
        /// Payloads the currently committed header references. On failure they must survive the rollback: an
        /// incoming descriptor that shares Key and Uid with one of them resolves to the same stored object, so
        /// "undo the copy" would delete live bytes rather than the copy. Null/empty for a brand-new file, where
        /// nothing is committed yet and every incoming payload is genuinely an orphan.
        /// </param>
        private async Task CopyPayloadsAndThumbnailsToLongTermStorage(InternalDriveFileId originFile, InternalDriveFileId targetFile,
            List<PayloadDescriptor> descriptors, StorageDrive drive, StagingArea sourceArea,
            List<PayloadDescriptor> committedPayloads = null)
        {
            // [UploadTiming] Diagnostic: time the whole commit-to-long-term loop (payloads uploaded sequentially here).
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var payloadCount = descriptors?.Count ?? 0;
            var thumbnailCount = descriptors?.Sum(d => d.Thumbnails?.Count() ?? 0) ?? 0;
            _logger.LogInformation(
                "[UploadTiming] Commit-to-long-term START fileId:{fileId} payloads:{payloads} thumbnails:{thumbnails}",
                targetFile.FileId, payloadCount, thumbnailCount);
            try
            {
                foreach (var descriptor in descriptors)
                {
                    await CopyPayloadAndThumbnailsToLongTermStorage(originFile, targetFile, drive, descriptor, sourceArea);
                }

                _logger.LogInformation(
                    "[UploadTiming] Commit-to-long-term DONE fileId:{fileId} payloads:{payloads} thumbnails:{thumbnails} totalMs:{totalMs}",
                    targetFile.FileId, payloadCount, thumbnailCount, totalSw.ElapsedMilliseconds);
            }
            catch
            {
                _logger.LogWarning(
                    "[UploadTiming] Commit-to-long-term FAILED fileId:{fileId} totalMs:{totalMs}",
                    targetFile.FileId, totalSw.ElapsedMilliseconds);
                await longTermStorageManager.TryHardDeleteListOfPayloadFiles(drive, targetFile.FileId,
                    PayloadStorage.ExcludeStillLive(descriptors, committedPayloads));
                throw;
            }
        }

        /// <summary>
        /// Copies payload and thumbs to long term storage
        /// </summary>
        private async Task CopyPayloadAndThumbnailsToLongTermStorage(InternalDriveFileId originFile, InternalDriveFileId targetFile,
            StorageDrive drive, PayloadDescriptor descriptor, StagingArea sourceArea)
        {
            if (sourceArea == StagingArea.LongTerm)
            {
                // The peer receive path already streamed this payload straight to long-term under
                // originFile.FileId (no inbox folder). For a brand-new file the incoming fileId IS the final fileId,
                // so the bytes are already at their destination and there is nothing to copy. For an
                // overwrite/update the target fileId differs, so relocate within long-term.
                if (originFile.FileId != targetFile.FileId)
                {
                    await longTermStorageManager.MovePayloadWithinLongTermAsync(drive, originFile.FileId, targetFile.FileId,
                        descriptor);
                }

                return;
            }

            var sourceStore = ResolveStore(sourceArea);

            var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
            var sourceFilePath = Path.Combine(StagingRoot(sourceArea, drive), TenantPathManager.GetFilename(originFile.FileId, payloadExtension));

            // [UploadTiming] Diagnostic: time this single payload's copy to long-term storage.
            var payloadSw = System.Diagnostics.Stopwatch.StartNew();
            await longTermStorageManager.CopyPayloadToLongTermAsync(
                drive,
                targetFile.FileId,
                descriptor,
                sourceFilePath,
                sourceStore);
            _logger.LogInformation(
                "[UploadTiming] Payload copied to long-term fileId:{fileId} payloadKey:{key} elapsedMs:{elapsedMs}",
                targetFile.FileId, descriptor.Key, payloadSw.ElapsedMilliseconds);

            foreach (var thumb in descriptor.Thumbnails ?? [])
            {
                var thumbExt = TenantPathManager.GetThumbnailFileNameAndExtension(
                    descriptor.Key, descriptor.Uid, thumb.PixelWidth, thumb.PixelHeight);

                var sourceThumbnail = Path.Combine(StagingRoot(sourceArea, drive), TenantPathManager.GetFilename(originFile.FileId, thumbExt));
                // [UploadTiming] Diagnostic: time this single thumbnail's copy to long-term storage.
                var thumbSw = System.Diagnostics.Stopwatch.StartNew();
                await longTermStorageManager.CopyThumbnailToLongTermAsync(drive, targetFile.FileId, sourceThumbnail, descriptor,
                    thumb, sourceStore);
                _logger.LogInformation(
                    "[UploadTiming] Thumbnail copied to long-term fileId:{fileId} payloadKey:{key} {w}x{h} elapsedMs:{elapsedMs}",
                    targetFile.FileId, descriptor.Key, thumb.PixelWidth, thumb.PixelHeight, thumbSw.ElapsedMilliseconds);
            }
        }

        private async Task<List<string>> GetMissingPayloadsAsync(FileMetadata metadata)
        {
            var missingPayloads = new List<string>();
            var drive = await DriveManager.GetDriveAsync(metadata.File.DriveId);

            // special exception *eye roll*.  really need to root this feed thing out of the core
            if (drive.TargetDriveInfo == WellKnownAppDrives.FeedDrive)
            {
                return missingPayloads;
            }

            var fileId = metadata.File.FileId;
            foreach (var payloadDescriptor in metadata.Payloads ?? [])
            {
                bool payloadExists = await longTermStorageManager.PayloadExistsOnDiskAsync(drive, fileId, payloadDescriptor);
                if (!payloadExists)
                {
                    missingPayloads.Add(TenantPathManager.GetPayloadFileName(fileId, payloadDescriptor.Key, payloadDescriptor.Uid));
                }

                foreach (var thumbnailDescriptor in payloadDescriptor.Thumbnails ?? [])
                {
                    var thumbExists = await longTermStorageManager.ThumbnailExistsOnDiskAsync(drive, fileId, payloadDescriptor,
                        thumbnailDescriptor);
                    if (!thumbExists)
                    {
                        missingPayloads.Add(TenantPathManager.GetThumbnailFileName(fileId, payloadDescriptor.Key, payloadDescriptor.Uid,
                            thumbnailDescriptor.PixelWidth,
                            thumbnailDescriptor.PixelHeight));
                    }
                }
            }

            return missingPayloads;
        }

        public async Task AssertPayloadsExistOnFileSystemAsync(ServerFileHeader header)
        {
            var metadata = header.FileMetadata;
            if (metadata.PayloadsAreRemote)
            {
                return;
            }

            var sl = await GetMissingPayloadsAsync(metadata);

            if (sl.Count > 0)
            {
                _logger.LogError("File metadata ({file}) missing these payloads / thumbnails:[{pt}]",
                    metadata.File,
                    string.Join(",", sl));

                // throw new OdinFileHeaderHasCorruptPayloadException(
                //     $"File metadata ({metadata.File.ToString()}) missing these payloads / thumbnails:" +
                //     string.Join(",", sl));
            }
        }

        private async Task AssertDriveIsNotArchived(Guid driveId, IOdinContext odinContext)
        {
            var theDrive = await DriveManager.GetDriveAsync(driveId);

            if (theDrive == null)
            {
                return;
            }

            if (theDrive.IsArchived)
            {
                OdinValidationUtils.AssertNotNull(odinContext, "odinContext must not be null");

                if (!odinContext.Caller.HasMasterKey)
                {
                    throw new OdinClientException("Drive is archived", OdinClientErrorCode.InvalidDrive);
                }
            }
        }
    }
}