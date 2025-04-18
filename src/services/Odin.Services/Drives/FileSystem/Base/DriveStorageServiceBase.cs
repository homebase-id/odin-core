using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase(
        ILoggerFactory loggerFactory,
        IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        DriveManager driveManager,
        LongTermStorageManager longTermStorageManager,
        UploadStorageManager uploadStorageManager,
        IdentityDatabase db) : RequirePermissionsBase
    {
        private readonly ILogger<DriveStorageServiceBase> _logger = loggerFactory.CreateLogger<DriveStorageServiceBase>();

        protected override DriveManager DriveManager { get; } = driveManager;

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> of which the inheriting class manages
        /// </summary>
        public abstract FileSystemType GetFileSystemType();

        public async Task<SharedSecretEncryptedFileHeader> GetSharedSecretEncryptedHeader(InternalDriveFileId file,
            IOdinContext odinContext)
        {
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

            var payloadEncryptedKeyHeader = DriveFileUtility.GetPayloadEncryptedKeyHeader(
                serverFileHeader,
                payloadDescriptor,
                odinContext);

            return (serverFileHeader, payloadDescriptor, payloadEncryptedKeyHeader, true);
        }

        public Task<InternalDriveFileId> CreateInternalFileId(Guid driveId)
        {
            var df = new InternalDriveFileId()
            {
                FileId = longTermStorageManager.CreateFileId(),
                DriveId = driveId,
            };

            return Task.FromResult(df);
        }

        public async Task<(int originalRecipientCount, PagedResult<RecipientTransferHistoryItem> results)> GetTransferHistory(
            InternalDriveFileId file, IOdinContext odinContext)
        {
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext);
            if (serverFileHeader == null)
            {
                return (0, null);
            }

            var results = await longTermStorageManager.GetTransferHistory(file.DriveId, file.FileId);

            var pagedResults = new PagedResult<RecipientTransferHistoryItem>(PageOptions.All, 1, results);
            return (serverFileHeader.ServerMetadata.OriginalRecipientCount, pagedResults);
        }

        private async Task UpdateActiveFileHeaderInternal(InternalDriveFileId targetFile, ServerFileHeader header, bool keepSameVersionTag,
            IOdinContext odinContext,
            bool raiseEvent = false, bool ignoreFeedDistribution = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException(
                    "An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

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

            await WriteFileHeaderInternal(header,
                keepSameVersionTag); // WriteFileHeaderInternal sets the Created / Updated on header.FileMetadata

            //HACKed in for Feed drive -> Should become a data subscription check
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEventAsync(targetFile))
                {
                    await mediator.Publish(new DriveFileChangedNotification
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
            bool raiseEvent = false, bool keepSameVersionTag = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException(
                    "An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded
            metadata.FileState = FileState.Active;

            await WriteFileHeaderInternal(header, keepSameVersionTag: keepSameVersionTag); // sets the header.FileMetadata.Created/Updated

            //HACKed in for Feed drive -> should become data subscription
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEventAsync(targetFile))
                {
                    await mediator.Publish(new DriveFileAddedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                    });
                }
            }
        }

        public async Task<uint> WriteTempStream(TempFile tempFile, string extension, Stream stream, IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(tempFile.File.DriveId, odinContext);
            return await uploadStorageManager.WriteStream(tempFile, extension, stream);
        }

        /// <summary>
        /// Reads the whole file so be sure this is only used on small'ish files; ones you're ok with loaded fully into server-memory
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetAllFileBytesFromTempFile(TempFile tempFile, string extension, IOdinContext odinContext)
        {
            await AssertCanReadDriveAsync(tempFile.File.DriveId, odinContext);
            var bytes = await uploadStorageManager.GetAllFileBytes(tempFile, extension);
            return bytes;
        }

        public async Task<bool> TempFileExists(TempFile tempFile, string extension, IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsOwner();
            return await uploadStorageManager.TempFileExists(tempFile, extension);
        }

        /// <summary>
        /// Tests if additional payload or thumbnail files for the given file
        /// </summary>
        public async Task<bool> HasOrphanPayloads(InternalDriveFileId file, IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsOwner();
            var originalHeader = await this.GetServerFileHeaderInternal(file, odinContext);
            var metadata = originalHeader.FileMetadata;
            return await longTermStorageManager.HasOrphanPayloadsOrThumbnails(file, metadata.Payloads);
        }

        public async Task<byte[]> GetAllFileBytesFromTempFileForWriting(TempFile tempFile, string extension,
            IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(tempFile.File.DriveId, odinContext);
            return await uploadStorageManager.GetAllFileBytes(tempFile, extension);
        }

        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetThumbnailPayloadStreamAsync(InternalDriveFileId file,
            int width,
            int height,
            string payloadKey, UnixTimeUtcUnique payloadUid, IOdinContext odinContext, bool directMatchOnly = false)
        {
            await AssertCanReadDriveAsync(file.DriveId, odinContext);

            DriveFileUtility.AssertValidPayloadKey(payloadKey);

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
                    var s = longTermStorageManager.GetThumbnailStream(drive, file.FileId, width, height, payloadKey, payloadUid);
                    return (s, directMatchingThumb);
                }
                catch (OdinFileHeaderHasCorruptPayloadException)
                {
                    if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                    {
                        return (Stream.Null, directMatchingThumb);
                    }

                    throw;
                }
            }

            if (directMatchOnly)
            {
                return (Stream.Null, null);
            }

            //TODO: add more logic here to compare width and height separately or together
            var nextSizeUp = thumbs.FirstOrDefault(t => t.PixelHeight > height || t.PixelWidth > width);
            if (null == nextSizeUp)
            {
                nextSizeUp = thumbs.LastOrDefault();
                if (null == nextSizeUp)
                {
                    return (Stream.Null, null);
                }
            }

            try
            {
                var stream = longTermStorageManager.GetThumbnailStream(
                    drive,
                    file.FileId,
                    nextSizeUp.PixelWidth,
                    nextSizeUp.PixelHeight,
                    payloadKey, payloadUid);

                return (stream, nextSizeUp);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return (Stream.Null, nextSizeUp);
                }

                throw;
            }
        }

        public async Task<Guid> DeletePayload(InternalDriveFileId file, string key, Guid targetVersionTag, IOdinContext odinContext)
        {
            //Note: calling to get the file header, so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext);
            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.VersionTag, targetVersionTag);

            var descriptorIndex = header.FileMetadata.Payloads?.FindIndex(p => p.KeyEquals( key)) ?? -1;

            if (descriptorIndex == -1)
            {
                return Guid.Empty;
            }

            var descriptor = header.FileMetadata.Payloads![descriptorIndex];

            await DeletePayloadFromDiskInternal(file, descriptor);

            header.FileMetadata.Payloads!.RemoveAt(descriptorIndex);
            await UpdateActiveFileHeader(file, header, odinContext);
            return header.FileMetadata.VersionTag.GetValueOrDefault(); // this works because we pass header all the way
        }

        public async Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext)
        {
            return await CreateServerHeaderInternal(file, keyHeader, metadata, serverMetadata, odinContext);
        }

        private async Task<EncryptedKeyHeader> EncryptKeyHeader(Guid driveId, KeyHeader keyHeader, IOdinContext odinContext)
        {
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(driveId);

            (await this.DriveManager.GetDriveAsync(driveId)).AssertValidStorageKey(storageKey);

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        /*
        public async Task<bool> CallerHasPermissionToFile(InternalDriveFileId file, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var header = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());

            if (null == header)
            {
                _logger.LogDebug($"Permission check called on non-existing file {file}");
                return false;
            }

            return await driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);
        }
        */

        public async Task<bool> CallerHasPermissionToFile(ServerFileHeader header, IOdinContext odinContext)
        {
            if (null == header)
            {
                _logger.LogDebug($"Permission check called on null header");
                return false;
            }

            return await driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file, IOdinContext odinContext)
        {
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

        public async Task<PayloadStream> GetPayloadStreamAsync(InternalDriveFileId file, string key, FileChunk chunk,
            IOdinContext odinContext)
        {
            await AssertCanReadDriveAsync(file.DriveId, odinContext);
            DriveFileUtility.AssertValidPayloadKey(key);

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

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            try
            {
                var stream = await longTermStorageManager.GetPayloadStream(drive, file.FileId, descriptor, chunk);
                return new PayloadStream(descriptor, stream.Length, stream);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<bool> FileExists(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            return await longTermStorageManager.HeaderFileExists(drive, file.FileId, GetFileSystemType());
        }

        public async Task SoftDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext);

            var existingHeader = await this.GetServerFileHeaderInternal(file, odinContext);

            await WriteDeletedFileHeader(existingHeader, odinContext);
        }

        public async Task HardDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            await longTermStorageManager.HardDeleteAsync(drive, file.FileId);

            if (await ShouldRaiseDriveEventAsync(file))
            {
                await mediator.Publish(new DriveFileDeletedNotification
                {
                    IsHardDelete = true,
                    File = file,
                    ServerFileHeader = null,
                    SharedSecretEncryptedFileHeader = null,
                    OdinContext = odinContext,
                });
            }
        }

        public async Task CommitNewFile(TempFile originFile, KeyHeader keyHeader, FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload, IOdinContext odinContext, bool keepSameVersionTag = false)
        {
            await AssertCanWriteToDrive(originFile.File.DriveId, odinContext);
            var drive = await DriveManager.GetDriveAsync(originFile.File.DriveId);

            var targetFile = originFile.File;
            newMetadata.File = targetFile; // this is a new file so we can use the same fileId from the temp file
            serverMetadata.FileSystemType = GetFileSystemType();

            ServerFileHeader serverHeader;

            try
            {
                if (!ignorePayload.GetValueOrDefault(false))
                {
                    await ProcessPayloads(originFile, targetFile, newMetadata.Payloads ?? [], drive);
                }

                serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, newMetadata, serverMetadata, odinContext);
                await WriteNewFileHeader(targetFile, serverHeader, odinContext, keepSameVersionTag: keepSameVersionTag);
            }
            catch
            {
                await longTermStorageManager.DeleteUnassociatedTargetFiles(targetFile);
                throw;
            }
            finally
            {
                await uploadStorageManager.EnsureDeleted(originFile);
            }

            if (serverHeader != null && await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new DriveFileAddedNotification
                {
                    File = originFile.File,
                    ServerFileHeader = serverHeader,
                    OdinContext = odinContext,
                });
            }
        }

        public async Task OverwriteFile(TempFile originFile, InternalDriveFileId targetFile, KeyHeader keyHeader,
            FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload, IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Cannot overwrite file that does not exist", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            DriveFileUtility.AssertVersionTagMatch(existingServerHeader.FileMetadata.VersionTag, newMetadata.VersionTag);

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

            newMetadata.File = existingServerHeader.FileMetadata.File;
            //Note: our call to GetServerFileHeader earlier validates the existing
            serverMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            ServerFileHeader serverHeader;
            try
            {
                if (!ignorePayload.GetValueOrDefault(false))
                {
                    await ProcessPayloads(originFile, targetFile, newMetadata.Payloads ?? [], drive);
                }

                serverHeader = new ServerFileHeader()
                {
                    EncryptedKeyHeader = await EncryptKeyHeader(originFile.File.DriveId, keyHeader, odinContext),
                    FileMetadata = newMetadata,
                    ServerMetadata = serverMetadata
                };

                await WriteFileHeaderInternal(serverHeader);
            }
            finally
            {
                //paranoid cleanup
                if (!ignorePayload.GetValueOrDefault(false))
                {
                    //Since this method is a full overwrite, zombies are all payloads on the file being overwritten
                    var zombiePayloads = existingServerHeader.FileMetadata.Payloads;
                    await DeleteZombiePayloads(targetFile, zombiePayloads);
                    await uploadStorageManager.EnsureDeleted(originFile);
                }
            }

            if (serverHeader != null && await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    OdinContext = odinContext,
                });
            }
        }

        public async Task<Guid> UpdatePayloads(
            TempFile originFile,
            InternalDriveFileId targetFile,
            List<PayloadDescriptor> incomingPayloads,
            IOdinContext odinContext)
        {
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

            // zombies will be those payloads that we overwrite 
            var zombiePayloads = existingServerHeader.FileMetadata.Payloads
                .Where(existingPayload => incomingPayloads.Any(incomingPayload => incomingPayload.KeyEquals(existingPayload))).ToList();

            try
            {
                //Note: we do not delete existing payloads.  this feature adds or overwrites existing ones
                await ProcessPayloads(originFile, targetFile, incomingPayloads, drive);

                // get all the existing payloads that are not in the incoming list, we'll keep these
                var payloadsToKeep = existingServerHeader.FileMetadata.Payloads.Where(
                    existingPayload => incomingPayloads.All(incomingPayload => !incomingPayload.KeyEquals(existingPayload)));

                var allPayloads = incomingPayloads.Concat(payloadsToKeep).ToList();

                existingServerHeader.FileMetadata.Payloads = allPayloads;

                await WriteFileHeaderInternal(existingServerHeader);
            }
            finally
            {
                await DeleteZombiePayloads(targetFile, zombiePayloads);
                await uploadStorageManager.EnsureDeleted(originFile);
            }

            if (await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                });
            }

            return existingServerHeader.FileMetadata.VersionTag.GetValueOrDefault();
        }

        public async Task OverwriteMetadata(byte[] newKeyHeaderIv, InternalDriveFileId targetFile, FileMetadata newMetadata,
            ServerMetadata newServerMetadata,
            IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext);

            await OverwriteMetadataInternal(newKeyHeaderIv, existingServerHeader, newMetadata, newServerMetadata, odinContext);

            if (await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                });
            }
        }

        public async Task UpdateReactionSummary(InternalDriveFileId targetFile, ReactionSummary summary, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasAtLeastOneDrivePermission(targetFile.DriveId,
                DrivePermission.React,
                DrivePermission.Comment,
                DrivePermission.Write);

            summary ??= new ReactionSummary();

            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
            var existingHeader = await longTermStorageManager.GetServerFileHeader(drive, targetFile.FileId, GetFileSystemType());
            existingHeader.FileMetadata.ReactionPreview = summary;

            await longTermStorageManager.SaveReactionHistory(drive, targetFile.FileId, summary);

            if (await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(existingHeader, odinContext),
                    OdinContext = odinContext,
                });
            }
        }

        public async Task InitiateTransferHistoryAsync(InternalDriveFileId file, OdinId recipient, IOdinContext odinContext)
        {
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

            if (await ShouldRaiseDriveEventAsync(file))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = header,
                    OdinContext = odinContext,
                    IgnoreFeedDistribution = true,
                    IgnoreReactionPreviewCalculation = true
                });
            }
        }

        public async Task UpdateTransferHistory(InternalDriveFileId file, OdinId recipient, UpdateTransferHistoryData updateData,
            IOdinContext odinContext)
        {
            await AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);

            //
            // Get and validate the header
            //
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var header = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());
            AssertValidFileSystemType(header.ServerMetadata);

            _logger.LogDebug(
                "Updating transfer history success on file:{file} for recipient:{recipient} Version:{versionTag}\t Status:{status}\t IsInOutbox:{outbox}\t IsReadByRecipient: {isRead}",
                file,
                recipient,
                updateData.VersionTag,
                updateData.LatestTransferStatus,
                updateData.IsInOutbox,
                updateData.IsReadByRecipient);

            var (updatedHistory, modifiedTime) =
                await longTermStorageManager.SaveTransferHistoryAsync(drive.Id, file.FileId, recipient, updateData);

            // note: I'm just avoiding re-reading the file.
            header.ServerMetadata.TransferHistory = updatedHistory;
            header.FileMetadata.SetCreatedModifiedWithDatabaseValue(header.FileMetadata.Created, modifiedTime);

            if (await ShouldRaiseDriveEventAsync(file))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = header,
                    OdinContext = odinContext,
                    IgnoreFeedDistribution = true,
                    IgnoreReactionPreviewCalculation = true
                });
            }
        }

        // Feed drive hacks

        public async Task WriteNewFileToFeedDriveAsync(KeyHeader keyHeader, FileMetadata fileMetadata, IOdinContext odinContext)
        {
            // Method assumes you ensured the file was unique by some other method

            var feedDriveId = await DriveManager.GetDriveIdByAliasAsync(SystemDriveConstants.FeedDrive);
            await AssertCanWriteToDrive(feedDriveId.GetValueOrDefault(), odinContext);
            var file = await this.CreateInternalFileId(feedDriveId.GetValueOrDefault());

            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList.OwnerOnly,
                AllowDistribution = false
            };

            //we don't accept uniqueIds into the feed
            fileMetadata.AppData.UniqueId = null;

            var serverFileHeader = await this.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
            await this.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);
        }

        public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId file, FileMetadata fileMetadata, IOdinContext odinContext,
            bool bypassCallerCheck = false)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderInternal(file, odinContext);
            AssertValidFileSystemType(header.ServerMetadata);

            if (header == null)
            {
                throw new OdinClientException("Trying to update feed metadata for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            if (header.FileMetadata.FileState == FileState.Deleted)
            {
                // _logger.LogDebug("ReplaceFileMetadataOnFeedDrive - attempted to update a deleted file; this will be ignored.");
                return;
            }

            AssertValidFileSystemType(header.ServerMetadata);

            var feedDriveId = await DriveManager.GetDriveIdByAliasAsync(SystemDriveConstants.FeedDrive);
            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            if (!bypassCallerCheck) //eww: this allows the follower service to synchronize files when you start following someone.
            {
                //S0510
                if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
                {
                    _logger.LogDebug("ReplaceFileMetadataOnFeedDrive - header file sender ({sender}) did not match context sender {ctx}",
                        header.FileMetadata.SenderOdinId,
                        odinContext.GetCallerOdinIdOrFail());
                    throw new OdinSecurityException("Invalid caller");
                }
            }

            header.FileMetadata = fileMetadata;

            // Clearing the UID for any files that go into the feed drive because the feed drive 
            // comes from multiple channel drives from many different identities so there could be a clash
            header.FileMetadata.AppData.UniqueId = null;

            await this.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
            if (header.FileMetadata.ReactionPreview == null)
            {
                var drive = await DriveManager.GetDriveAsync(file.DriveId);
                await longTermStorageManager.DeleteReactionSummary(drive, file.FileId);
            }
            else
            {
                await UpdateReactionSummary(file, header.FileMetadata.ReactionPreview, odinContext);
            }
        }

        public async Task RemoveFeedDriveFile(InternalDriveFileId file, IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderInternal(file, odinContext);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await DriveManager.GetDriveIdByAliasAsync(SystemDriveConstants.FeedDrive);

            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            //S0510
            if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
            {
                throw new OdinSecurityException("Invalid caller");
            }

            await WriteDeletedFileHeader(header, odinContext);
        }

        public async Task UpdateReactionPreviewOnFeedDrive(InternalDriveFileId targetFile, ReactionSummary summary,
            IOdinContext odinContext)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext);
            var feedDriveId = await DriveManager.GetDriveIdByAliasAsync(SystemDriveConstants.FeedDrive);
            if (targetFile.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Cannot update reaction preview on this drive");
            }

            var drive = await DriveManager.GetDriveAsync(targetFile.DriveId);
            var existingHeader = await longTermStorageManager.GetServerFileHeader(drive, targetFile.FileId, GetFileSystemType());

            //S0510
            if (existingHeader.FileMetadata.SenderOdinId != odinContext.Caller.OdinId)
            {
                throw new OdinSecurityException("Invalid caller");
            }

            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            if (await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(existingHeader, odinContext),
                    OdinContext = odinContext,
                });
            }
        }

        public async Task UpdateActiveFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext,
            bool raiseEvent = false)
        {
            await UpdateActiveFileHeaderInternal(targetFile, header, false, odinContext, raiseEvent);
        }


        public async Task UpdateBatchAsync(TempFile originFile, InternalDriveFileId targetFile, BatchUpdateManifest manifest,
            IOdinContext odinContext)
        {
            List<PayloadDescriptor> DeleteFileReferencesFromHeader(ServerFileHeader existingHeader1)
            {
                var zombies = new List<PayloadDescriptor>();

                // Delete operations are for existing payloads, so we need to update the existing header
                foreach (var op in manifest.PayloadInstruction.Where(op => op.OperationType == PayloadUpdateOperationType.DeletePayload))
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
                foreach (var op in manifest.PayloadInstruction
                             .Where(op => op.OperationType == PayloadUpdateOperationType.AppendOrOverwrite))
                {
                    // Here look at the incoming payloads because we're adding a new one or overwriting
                    var descriptor = manifest.FileMetadata.GetPayloadDescriptor(op.Key, true, $"Could not find payload " +
                        $"with key {op.Key} in FileMetadata to " +
                        $"perform operation {op.OperationType}");

                    // Move the payload from the temp folder to the long term folder
                    await ProcessPayloadDescriptor(originFile, targetFile, storageDrive, descriptor);

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

                return zombies;
            }

            OdinValidationUtils.AssertNotEmptyGuid(manifest.NewVersionTag, nameof(manifest.NewVersionTag));
            var metadata = manifest.FileMetadata;
            metadata.AppData?.Validate();

            //
            // Validations
            //
            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext);
            if (null == existingHeader)
            {
                throw new OdinClientException("File being updated does not exist", OdinClientErrorCode.InvalidFile);
            }

            DriveFileUtility.AssertVersionTagMatch(metadata.VersionTag, existingHeader.FileMetadata.VersionTag);

            if (existingHeader.FileMetadata.IsEncrypted)
            {
                var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(existingHeader.FileMetadata.File.DriveId);
                var existingKeyHeader = existingHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

                if (!ByteArrayUtil.EquiByteArrayCompare(manifest.KeyHeader.AesKey.GetKey(), existingKeyHeader.AesKey.GetKey()))
                {
                    throw new OdinClientException("When updating a file, you cannot change the AesKey as it might " +
                                                  "invalidate one or more payloads.  Re-upload the entire file if you wish " +
                                                  "to rotate keys", OdinClientErrorCode.InvalidKeyHeader);
                }

                if (ByteArrayUtil.EquiByteArrayCompare(manifest.KeyHeader.Iv, existingKeyHeader.Iv))
                {
                    throw new OdinClientException("When updating a file, you must change the Iv", OdinClientErrorCode.InvalidKeyHeader);
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

            try
            {
                zombies.AddRange(await ProcessAppendOrOverwrite(drive, existingHeader));
                zombies.AddRange(DeleteFileReferencesFromHeader(existingHeader));

                existingHeader.FileMetadata.VersionTag = manifest.NewVersionTag;
                await OverwriteMetadataInternal(manifest.KeyHeader.Iv, existingHeader, manifest.FileMetadata,
                    manifest.ServerMetadata, odinContext, manifest.NewVersionTag);
            }
            finally
            {
                await DeleteZombiePayloads(targetFile, zombies);
                await uploadStorageManager.EnsureDeleted(originFile);
            }

            if (await ShouldRaiseDriveEventAsync(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    OdinContext = odinContext,
                    IgnoreFeedDistribution = false
                });
            }
        }

        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataTags(InternalDriveFileId file,
            Guid targetVersionTag,
            List<Guid> newTags,
            IOdinContext odinContext)
        {
            OdinValidationUtils.AssertIsTrue(file.IsValid(), "file is invalid");
            OdinValidationUtils.AssertIsTrue(newTags.Count <= 50, "max local tags is 50");

            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderForWriting(file, odinContext);
            if (null == header)
            {
                throw new OdinClientException("Cannot update local app data for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.LocalAppData?.VersionTag ?? Guid.Empty, targetVersionTag);

            var newVersionTag = DriveFileUtility.CreateVersionTag();

            var mergedMetadata = new LocalAppMetadata
            {
                Iv = header.FileMetadata.LocalAppData?.Iv,
                VersionTag = newVersionTag,
                Content = header.FileMetadata.LocalAppData?.Content,
                Tags = newTags,
            };

            await longTermStorageManager.SaveLocalMetadataTagsAsync(file, mergedMetadata);

            var updatedHeader = await GetServerFileHeaderForWriting(file, odinContext);
            if (await ShouldRaiseDriveEventAsync(file))
            {
                await mediator.Publish(new DriveFileChangedNotification
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

            await AssertCanWriteToDrive(file.DriveId, odinContext);
            var header = await GetServerFileHeaderForWriting(file, odinContext);
            if (null == header)
            {
                throw new OdinClientException("Cannot update local app data for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.LocalAppData?.VersionTag ?? Guid.Empty, targetVersionTag);


            if (header.FileMetadata.IsEncrypted && !ByteArrayUtil.IsStrongKey(initVector))
            {
                throw new OdinClientException("A string IV is required when the target file is encrypted");
            }

            var newVersionTag = DriveFileUtility.CreateVersionTag();

            var mergedMetadata = new LocalAppMetadata
            {
                VersionTag = newVersionTag,
                Iv = initVector,
                Content = newContent,
                Tags = header.FileMetadata.LocalAppData?.Tags ?? [],
            };

            mergedMetadata.Validate();

            await longTermStorageManager.SaveLocalMetadataAsync(file, mergedMetadata);

            var updatedHeader = await GetServerFileHeaderForWriting(file, odinContext);
            if (await ShouldRaiseDriveEventAsync(file))
            {
                await mediator.Publish(new DriveFileChangedNotification
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

        private async Task WriteFileHeaderInternal(ServerFileHeader header, bool keepSameVersionTag = false)
        {
            await AssertPayloadsExistOnFileSystem(header.FileMetadata);

            // Note: these validations here are just-in-case checks; however at this point many
            // other operations will have occured, so these checks also exist in the upload validation

            header.FileMetadata.AppData?.Validate();

            if (!keepSameVersionTag)
            {
                header.FileMetadata.VersionTag = DriveFileUtility.CreateVersionTag();
            }

            var json = OdinSystemSerializer.Serialize(header);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            var payloadDiskUsage = header.FileMetadata.Payloads?.Sum(p => p.BytesWritten) ?? 0;
            var thumbnailDiskUsage = header.FileMetadata.Payloads?
                .SelectMany(p => p.Thumbnails ?? new List<ThumbnailDescriptor>())
                .Sum(pp => pp.BytesWritten) ?? 0;
            header.ServerMetadata.FileByteCount = payloadDiskUsage + thumbnailDiskUsage + jsonBytes.Length;

            var drive = await DriveManager.GetDriveAsync(header.FileMetadata.File.DriveId);

            await longTermStorageManager.SaveFileHeader(drive,
                header); // SaveFileHeader updates the Created / Updated fields in the header.FileMetadata
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

        private async Task<bool> ShouldRaiseDriveEventAsync(InternalDriveFileId file)
        {
            return file.DriveId != (await DriveManager.GetDriveIdByAliasAsync(SystemDriveConstants.TransientTempDrive));
        }

        private async Task WriteDeletedFileHeader(ServerFileHeader existingHeader, IOdinContext odinContext)
        {
            var file = existingHeader.FileMetadata.File;

            var deletedServerFileHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = existingHeader.EncryptedKeyHeader,
                FileMetadata = new FileMetadata(existingHeader.FileMetadata.File)
                {
                    FileState = FileState.Deleted,
                    Updated = UnixTimeUtc.Now().milliseconds,
                    GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId
                },
                ServerMetadata = existingHeader.ServerMetadata
            };

            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            await using (var tx = await db.BeginStackedTransactionAsync())
            {
                longTermStorageManager.HardDeleteAllPayloadFiles(drive, file.FileId);
                await WriteFileHeaderInternal(deletedServerFileHeader);
                await longTermStorageManager.DeleteReactionSummary(drive, deletedServerFileHeader.FileMetadata.File.FileId);
                await longTermStorageManager.DeleteTransferHistoryAsync(drive, deletedServerFileHeader.FileMetadata.File.FileId);
                tx.Commit();
            }

            if (await ShouldRaiseDriveEventAsync(file))
            {
                await mediator.Publish(new DriveFileDeletedNotification
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

        private async Task<ServerFileHeader> GetServerFileHeaderInternal(InternalDriveFileId file, IOdinContext odinContext)
        {
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            var header = await longTermStorageManager.GetServerFileHeader(drive, file.FileId, GetFileSystemType());

            if (null == header)
            {
                return null;
            }

            await driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);

            return header;
        }

        private async Task OverwriteMetadataInternal(byte[] keyHeaderIv, ServerFileHeader existingServerHeader, FileMetadata newMetadata,
            ServerMetadata newServerMetadata,
            IOdinContext odinContext, Guid? newVersionTag = null)
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

            if (newVersionTag == null)
            {
                DriveFileUtility.AssertVersionTagMatch(existingServerHeader.FileMetadata.VersionTag, newMetadata.VersionTag);
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
            if (newVersionTag == null)
            {
                await WriteFileHeaderInternal(existingServerHeader); // Sets header.FileMetadata.Created/Updated
            }
            else
            {
                existingServerHeader.FileMetadata.VersionTag = newVersionTag.Value;
                await WriteFileHeaderInternal(existingServerHeader, keepSameVersionTag: true); // Sets header.FileMetadata.Created/Updated
            }
        }

        private async Task DeletePayloadFromDiskInternal(InternalDriveFileId file, PayloadDescriptor descriptor)
        {
            var drive = await DriveManager.GetDriveAsync(file.DriveId);

            // Delete the thumbnail files for this payload
            foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
            {
                longTermStorageManager.HardDeleteThumbnailFile(drive, file.FileId, descriptor.Key, descriptor.Uid, thumb.PixelWidth,
                    thumb.PixelHeight);
            }

            // Delete the payload file
            longTermStorageManager.HardDeletePayloadFile(drive, file.FileId, descriptor.Key, descriptor.Uid);
        }

        private async Task ProcessPayloads(TempFile originFile, InternalDriveFileId targetFile,
            List<PayloadDescriptor> descriptors, StorageDrive drive)
        {
            foreach (var descriptor in descriptors)
            {
                await ProcessPayloadDescriptor(originFile, targetFile, drive, descriptor);
            }
        }

        private async Task ProcessPayloadDescriptor(TempFile originFile, InternalDriveFileId targetFile, StorageDrive drive,
            PayloadDescriptor descriptor)
        {
            var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
            var sourceFilePath = await uploadStorageManager.GetPath(originFile, payloadExtension);
            longTermStorageManager.MovePayloadToLongTerm(drive, targetFile.FileId, descriptor, sourceFilePath);

            foreach (var thumb in descriptor.Thumbnails ?? [])
            {
                var thumbExt = DriveFileUtility.GetThumbnailFileExtension(
                    descriptor.Key, descriptor.Uid, thumb.PixelWidth, thumb.PixelHeight);

                var sourceThumbnail = await uploadStorageManager.GetPath(originFile, thumbExt);
                longTermStorageManager.MoveThumbnailToLongTerm(drive, targetFile.FileId, sourceThumbnail, descriptor, thumb);
            }
        }

        private async Task AssertPayloadsExistOnFileSystem(FileMetadata metadata)
        {
            var drive = await DriveManager.GetDriveAsync(metadata.File.DriveId);

            // special exception *eye roll*.  really need to root this feed thing out of the core
            if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
            {
                return;
            }

            var fileId = metadata.File.FileId;
            foreach (var payloadDescriptor in metadata.Payloads ?? [])
            {
                bool payloadExists = longTermStorageManager.PayloadExistsOnDisk(drive, fileId, payloadDescriptor);
                if (!payloadExists)
                {
                    throw new OdinFileHeaderHasCorruptPayloadException(
                        $"File metadata ({metadata.File.ToString()}) defines payload [key:{payloadDescriptor.Key} " +
                        $"uid:{payloadDescriptor.Uid}] but the payload-file does not exist on disk.]");
                }

                foreach (var thumbnailDescriptor in payloadDescriptor.Thumbnails ?? [])
                {
                    var thumbExists = longTermStorageManager.ThumbnailExistsOnDisk(drive, fileId, payloadDescriptor, thumbnailDescriptor);
                    if (!thumbExists)
                    {
                        throw new OdinFileHeaderHasCorruptPayloadException(
                            $"File metadata [{metadata.File.ToString()}] defines payload [key:{payloadDescriptor.Key} " +
                            $"uid:{payloadDescriptor.Uid}] with thumbnail " +
                            $"[size={thumbnailDescriptor.PixelWidth}x{thumbnailDescriptor.PixelHeight}] but the thumbnail-file" +
                            $" does not exist on disk.]");
                    }
                }
            }
        }

        private async Task DeleteZombiePayloads(InternalDriveFileId file, List<PayloadDescriptor> deadPayloads)
        {
            var drive = await DriveManager.GetDriveAsync(file.DriveId);
            longTermStorageManager.HardDeleteDeadPayloadFiles(drive, file.FileId, deadPayloads);
        }
    }
}