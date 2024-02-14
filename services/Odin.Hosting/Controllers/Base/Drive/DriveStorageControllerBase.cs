using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Base.SharedTypes;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Core.Services.Util;
using Odin.Hosting.ApiExceptions.Client;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base class for any endpoint reading drive storage
    /// </summary>
    public abstract class DriveStorageControllerBase(
        // ILogger logger,
        FileSystemResolver fileSystemResolver,
        IPeerTransferService peerTransferService) : OdinControllerBase
    {
        // private readonly ILogger _logger = logger;

        /// <summary>
        /// Returns the file header
        /// </summary>
        protected async Task<IActionResult> GetFileHeader(ExternalFileIdentifier request)
        {
            var result = await this.GetHttpFileSystemResolver().ResolveFileSystem().Storage.GetSharedSecretEncryptedHeader(MapToInternalFile(request));

            if (result == null)
            {
                return NotFound();
            }

            // No caching on header
            // AddGuestApiCacheHeader();

            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        protected async Task<IActionResult> GetPayloadStream(GetPayloadRequest request)
        {
            DriveFileUtility.AssertValidPayloadKey(request.Key);

            var file = MapToInternalFile(request.File);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();

            var (header, payloadDescriptor, encryptedKeyHeader, fileExists) =
                await fs.Storage.GetPayloadSharedSecretEncryptedKeyHeader(file, request.Key);

            if (!fileExists)
            {
                return NotFound();
            }

            var payloadStream = await fs.Storage.GetPayloadStream(file, request.Key, request.Chunk);
            if (payloadStream == null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Append(HttpHeaderConstants.AcceptRanges, "bytes");
            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.IsEncrypted.ToString());
            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadKey, payloadStream.Key);
            HttpContext.Response.Headers.LastModified = payloadDescriptor.GetLastModifiedHttpHeaderValue();
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());

            if (null != request.Chunk)
            {
                var payloadSize = header.FileMetadata.Payloads.SingleOrDefault(p => p.Key == request.Key)?.BytesWritten ??
                                  throw new OdinSystemException("Invalid payload key");

                var to = request.Chunk.Length == int.MaxValue ? payloadSize - 1 : request.Chunk.Start + request.Chunk.Length - 1;

                // Sanity
                if (to >= payloadSize)
                {
                    throw new RequestedRangeNotSatisfiableException($"{to} >= {payloadSize}");
                }

                HttpContext.Response.Headers.Append("Content-Range",
                    new ContentRangeHeaderValue(request.Chunk.Start, to, payloadSize)
                        .ToString());
            }

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(payloadStream.Stream, payloadStream.ContentType);

            return result;
        }

        /// <summary>
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        protected async Task<IActionResult> GetThumbnail(GetThumbnailRequest request)
        {
            DriveFileUtility.AssertValidPayloadKey(request.PayloadKey);

            var file = MapToInternalFile(request.File);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();

            var (header, payloadDescriptor, encryptedKeyHeaderForPayload, fileExists) =
                await fs.Storage.GetPayloadSharedSecretEncryptedKeyHeader(file, request.PayloadKey);

            if (!fileExists)
            {
                return NotFound();
            }

            var (thumbPayload, thumbHeader) = await fs.Storage.GetThumbnailPayloadStream(file,
                request.Width, request.Height, request.PayloadKey, payloadDescriptor.Uid, request.DirectMatchOnly);

            if (thumbPayload == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata!.IsEncrypted.ToString());
            HttpContext.Response.Headers.LastModified = payloadDescriptor.GetLastModifiedHttpHeaderValue();
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, thumbHeader.ContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeaderForPayload.ToBase64());

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(thumbPayload, header.FileMetadata.IsEncrypted
                ? "application/octet-stream"
                : thumbHeader.ContentType);

            return result;
        }

        /// <summary>
        /// Deletes a file and sends delete linked file requests to all recipient if specified
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected async Task<IActionResult> DeleteFile(DeleteFileRequest request)
        {
            var result = await PerformFileDelete(request);
            return new JsonResult(result);
        }

        protected async Task<IActionResult> DeleteFilesByGroupIdBatch(DeleteFilesByGroupIdBatchRequest batchRequest)
        {
            var deleteBatchFinalResult = new DeleteFilesByGroupIdBatchResult()
            {
                Results = new List<DeleteFileByGroupIdResult>()
            };

            //Firstly resolve all drives to ensure we have access to do a complete deletion
            foreach (var request in batchRequest.Requests)
            {
                var driveId = OdinContext.PermissionsContext.GetDriveId(request.TargetDrive);
                OdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }

            foreach (var request in batchRequest.Requests)
            {
                var groupIdToBeDeleted = request.GroupId;

                var qp = new FileQueryParams()
                {
                    TargetDrive = request.TargetDrive,
                    GroupId = new List<Guid>() { groupIdToBeDeleted }
                };

                var options = new QueryBatchResultOptions()
                {
                    IncludeHeaderContent = false,
                    MaxRecords = int.MaxValue
                };

                var driveId = OdinContext.PermissionsContext.GetDriveId(request.TargetDrive);

                var queryResults = await GetHttpFileSystemResolver().ResolveFileSystem()
                    .Query.GetBatch(driveId, qp, options);

                //
                // Delete the batch resulting from the query
                //
                var deleteBatch = new DeleteFileIdBatchRequest()
                {
                    Requests = queryResults.SearchResults.Select(sr => new DeleteFileRequest()
                    {
                        File = new ExternalFileIdentifier()
                        {
                            FileId = sr.FileId,
                            TargetDrive = request.TargetDrive
                        },
                        Recipients = request.Recipients
                    }).ToList()
                };

                var batchResults = await PerformDeleteFileIdBatch(deleteBatch);

                deleteBatchFinalResult.Results.Add(new DeleteFileByGroupIdResult()
                {
                    GroupId = groupIdToBeDeleted,
                    DeleteFileResults = batchResults.Results
                });
            }

            return new JsonResult(deleteBatchFinalResult);
        }

        protected async Task<IActionResult> DeleteFileIdBatch(DeleteFileIdBatchRequest batchRequest)
        {
            //Firstly resolve all drives to ensure we have access to do a complete deletion
            foreach (var request in batchRequest.Requests)
            {
                var driveId = OdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
                OdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }

            var batchResult = await PerformDeleteFileIdBatch(batchRequest);
            return new JsonResult(batchResult);
        }

        protected async Task<DeletePayloadResult> DeletePayload(DeletePayloadRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid delete payload request");
            }

            DriveFileUtility.AssertValidPayloadKey(request.Key);
            if (request.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag", OdinClientErrorCode.MissingVersionTag);
            }

            var file = MapToInternalFile(request.File);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key, request.VersionTag.GetValueOrDefault())
            };
        }

        private async Task<DeleteFileIdBatchResult> PerformDeleteFileIdBatch(DeleteFileIdBatchRequest batchRequest)
        {
            var results = new List<DeleteFileResult>();
            foreach (var request in batchRequest.Requests)
            {
                var r = await PerformFileDelete(request);
                results.Add(r);
            }

            var batchResult = new DeleteFileIdBatchResult()
            {
                Results = results
            };

            return batchResult;
        }

        private async Task<DeleteFileResult> PerformFileDelete(DeleteFileRequest request)
        {
            var driveId = OdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
            var requestRecipients = request.Recipients;

            OdinValidationUtils.AssertValidRecipientList(request.Recipients, allowEmpty: true);

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            var result = new DeleteFileResult()
            {
                File = request.File,
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            var fs = fileSystemResolver.ResolveFileSystem(file);

            var header = await fs.Storage.GetServerFileHeader(file);
            if (header == null)
            {
                result.LocalFileNotFound = true;
                return result;
            }

            var recipients = requestRecipients ?? new List<string>();
            if (recipients.Any() && header.FileMetadata.GlobalTransitId.HasValue)
            {
                var remoteGlobalTransitIdentifier = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = request.File.TargetDrive
                };

                //send the deleted file
                var responses = await peerTransferService.SendDeleteFileRequest(remoteGlobalTransitIdentifier,
                    new FileTransferOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal
                    },
                    recipients);

                result.RecipientStatus = responses;
            }

            await fs.Storage.SoftDeleteLongTermFile(file);
            result.LocalFileDeleted = true;
            return result;
        }
    }
}