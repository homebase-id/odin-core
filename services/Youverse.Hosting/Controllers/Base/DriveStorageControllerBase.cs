using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Quartz.Util;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary>
    /// Base class for any endpoint reading drive storage
    /// </summary>
    public abstract class DriveStorageControllerBase : OdinControllerBase
    {
        private readonly ITransitService _transitService;
        private readonly FileSystemResolver _fileSystemResolver;

        protected DriveStorageControllerBase(FileSystemResolver fileSystemResolver, ITransitService transitService)
        {
            _fileSystemResolver = fileSystemResolver;
            _transitService = transitService;
        }

        /// <summary>
        /// Returns the file header
        /// </summary>
        protected async Task<IActionResult> GetFileHeader(ExternalFileIdentifier request)
        {
            var result = await this.GetFileSystemResolver().ResolveFileSystem().Storage.GetSharedSecretEncryptedHeader(MapToInternalFile(request));

            if (result == null)
            {
                return NotFound();
            }

            AddCacheHeader();
            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        protected async Task<IActionResult> GetPayloadStream(GetPayloadRequest request)
        {
            var file = MapToInternalFile(request.File);

            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            var payload = await fs.Storage.GetPayloadStream(file, request.Chunk);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);
            AddCacheHeader();
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        /// <summary>
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        protected async Task<IActionResult> GetThumbnail(GetThumbnailRequest request)
        {
            var file = MapToInternalFile(request.File);

            var fs = this.GetFileSystemResolver().ResolveFileSystem();
            var payload = await fs.Storage.GetThumbnailPayloadStream(file, request.Width, request.Height);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);

            AddCacheHeader();
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        /// <summary>
        /// Deletes a file and sends delete linked file requests to all recipient if specified
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected async Task<IActionResult> DeleteFile(DeleteFileRequest request)
        {
            var driveId = DotYouContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
            var requestRecipients = request.Recipients;

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            var result = new DeleteLinkedFileResult()
            {
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            var fs = _fileSystemResolver.ResolveFileSystem(file);

            var header = await fs.Storage.GetServerFileHeader(file);
            if (header == null)
            {
                result.LocalFileNotFound = true;
                return new JsonResult(result);
            }

            var recipients = requestRecipients ?? new List<string>();
            if (recipients.Any())
            {
                if (header.FileMetadata.GlobalTransitId.HasValue)
                {
                    var remoteGlobalTransitIdentifier = new GlobalTransitIdFileIdentifier()
                    {
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                        TargetDrive = request.File.TargetDrive
                    };

                    //send the deleted file
                    var map = await _transitService.SendDeleteLinkedFileRequest(remoteGlobalTransitIdentifier,
                        new SendFileOptions()
                        {
                            FileSystemType = header.ServerMetadata.FileSystemType,
                            TransferFileType = TransferFileType.Normal
                        },
                        recipients);

                    foreach (var (key, value) in map)
                    {
                        switch (value)
                        {
                            case TransitResponseCode.AcceptedIntoInbox:
                                result.RecipientStatus.Add(key, DeleteLinkedFileStatus.RequestAccepted);
                                break;

                            case TransitResponseCode.Rejected:
                            case TransitResponseCode.QuarantinedPayload:
                            case TransitResponseCode.QuarantinedSenderNotConnected:
                                result.RecipientStatus.Add(key, DeleteLinkedFileStatus.RequestRejected);
                                break;

                            default:
                                throw new YouverseSystemException($"Unknown TransitResponseCode {value}");
                        }
                    }
                }
            }

            await fs.Storage.SoftDeleteLongTermFile(file);
            result.LocalFileDeleted = true;

            if (result.LocalFileNotFound)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }
        
        protected async Task<IActionResult> DeleteThumbnail(GetThumbnailRequest request)
        {
            var file = MapToInternalFile(request.File);

            var fs = this.GetFileSystemResolver().ResolveFileSystem();
            await fs.Storage.DeleteThumbnail(file, request.Width, request.Height);
            return Ok();
        }

        protected async Task<IActionResult> DeletePayload(GetPayloadRequest request)
        {
            var file = MapToInternalFile(request.File);
            var fs = this.GetFileSystemResolver().ResolveFileSystem();
            await fs.Storage.DeletePayload(file);
            return Ok();
        }

        private void AddCacheHeader()
        {
            if (DotYouContext.AuthContext == ClientTokenConstants.YouAuthScheme)
            {
                this.Response.Headers.Add("Cache-Control", "max-age=3600");
            }
        }
    }

    public class UploadMetadataRequest
    {
        public StorageOptions StorageOptions { get; set; }
        public UploadFileMetadata UploadFileMetadata { get; set; }
    }
}