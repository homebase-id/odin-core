using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Drives.Management;
using Odin.Services.Util;
using Odin.Core.Util;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base API Controller for uploading multipart streams
    /// </summary>
    public abstract class V1DriveUploadControllerBase(ILogger logger, DriveManager driveManager, FileSystemResolver fileSystemResolver)
        : OdinControllerBase
    {
        /// <summary>
        /// Receives a stream for a new file being uploaded or existing file being overwritten
        /// </summary>
        protected Task<UploadResult> ReceiveNewFileStream()
        {
            return ReceiveNewFileStreamInternal(null);
        }

        /// <summary>
        /// Version for v2 where the driveId is passed in
        /// </summary>
        protected Task<UploadResult> ReceiveNewFileStreamV2(Guid driveId)
        {
            return ReceiveNewFileStreamInternal(driveId);
        }

        private async Task<UploadResult> ReceiveNewFileStreamInternal(Guid? driveId)
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException(
                    "Data is not multi-part content",
                    OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var driveUploadService = GetHttpFileSystemResolver().ResolveFileSystemWriter();

            try
            {
                return await ProcessNewFileUpload(reader, driveUploadService, driveId);
            }
            finally
            {
                try
                {
                    await driveUploadService.CleanupStagingFiles(WebOdinContext);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failure during file cleanup");
                }
            }
        }

        protected Task<FileUpdateResult> ReceiveFileUpdate()
        {
            return ReceiveFileUpdateInternal(driveId: null, fileId: null);
        }

        protected Task<FileUpdateResult> ReceiveFileUpdateV2(Guid driveId, Guid? fileId)
        {
            return ReceiveFileUpdateInternal(driveId, fileId, null);
        }

        protected Task<FileUpdateResult> ReceiveFileUpdateByUniqueIdV2(Guid driveId, Guid uniqueId)
        {
            return ReceiveFileUpdateInternal(driveId, null, uniqueId);
        }

        private async Task<FileUpdateResult> ReceiveFileUpdateInternal(Guid? driveId, Guid? fileId, Guid? uniqueId = null)
        {
            FileSystemType fileSystemType;
            FileSystemUpdateWriterBase updateWriter = null;

            try
            {
                if (!IsMultipartContentType(HttpContext.Request.ContentType))
                {
                    throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
                }

                var boundary = GetBoundary(HttpContext.Request.ContentType);
                var reader = new MultipartReader(boundary, HttpContext.Request.Body);

                var section = await ReadNextMultipartSectionAsync(reader);
                AssertIsPart(section, MultipartUploadParts.Instructions);

                var instructionSet = await BoundedRead.DeserializeAsync<FileUpdateInstructionSet>(section!.Body);

                //v2 reads from the driveId and fileId params, so we overwrite it. this stops callers
                //from being bound to the TargetDrive type
                if (WebOdinContext.ApiVersion == 2)
                {
                    OdinValidationUtils.AssertNotEmptyGuid(driveId.GetValueOrDefault(), "Version 2 requires DriveId to be set");
                    var theDrive = await driveManager.GetDriveAsync(driveId.GetValueOrDefault());

                    //we might be coming in via fileId or uniqueid so handle that here for v2
                    //TODO: clean up target drive when we remove v1 from the system
                    if (fileId.HasValue)
                    {
                        instructionSet.File = new FileIdentifier
                        {
                            FileId = fileId.GetValueOrDefault(),
                            TargetDrive = theDrive!.TargetDriveInfo,
                            DriveId = driveId.GetValueOrDefault()
                        };
                        
                    }
                    else if (uniqueId.HasValue)
                    {
                        instructionSet.File = new FileIdentifier
                        {
                            UniqueId = uniqueId.GetValueOrDefault(),
                            TargetDrive = theDrive!.TargetDriveInfo,
                            DriveId = driveId.GetValueOrDefault()
                        };
                    }
                    else
                    {
                        throw new OdinClientException("Must provide either FileId or UniqueId to identify the file to update", OdinClientErrorCode.MissingUploadData);
                    }

                    var fs = await fileSystemResolver.ResolveFileSystem(instructionSet.File, WebOdinContext);
                    updateWriter = this.GetHttpFileSystemResolver().ResolveFileSystemUpdateWriter();
                    fileSystemType = fs.Storage.GetFileSystemType();
                }
                else
                {
                    // old way
                    fileSystemType = this.GetHttpFileSystemResolver().GetFileSystemType();
                    updateWriter = this.GetHttpFileSystemResolver().ResolveFileSystemUpdateWriter();
                }

                await updateWriter.StartFileUpdateAsync(instructionSet, fileSystemType, WebOdinContext);

                //
                // Firstly, collect everything and store in the temp drive
                //
                await ReadUploadPartsAsync(reader, updateWriter, updateWriter.AddMetadata);

                var result = await updateWriter.FinalizeFileUpdate(WebOdinContext);
                return result;
            }
            finally
            {
                try
                {
                    if (updateWriter != null)
                    {
                        await updateWriter.CleanupStagingFiles(WebOdinContext);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failure during file cleanup");
                }
            }
        }

        private async Task<UploadResult> ProcessNewFileUpload(MultipartReader reader,
            FileSystemStreamWriterBase driveUploadService,
            Guid? driveIdForV2 = null)
        {
            var section = await ReadNextMultipartSectionAsync(reader);
            AssertIsPart(section, MultipartUploadParts.Instructions);
            try
            {
                logger.LogDebug("ReceiveFileStream: StartUpload");
                var instructionSet = await BoundedRead.DeserializeAsync<UploadInstructionSet>(section!.Body);

                Guid driveId;
                // v2 reads from driveId.  we will remove .Drive when we remove v1
                if (WebOdinContext.ApiVersion == 2)
                {
                    driveId = driveIdForV2.GetValueOrDefault();
                    if (instructionSet.StorageOptions.OverwriteFileId.HasValue)
                    {
                        throw new OdinClientException("Cannot use OverwriteFileId in version 2; use PATCH method instead");
                    }
                }
                else
                {
                    driveId = instructionSet.StorageOptions.Drive.Alias.Value;
                }

                OdinValidationUtils.AssertNotEmptyGuid(driveId, "Invalid Drive Id");
                await driveUploadService.StartUpload(driveId, instructionSet, WebOdinContext);
            }
            catch (JsonException e)
            {
                throw new OdinClientException($"JSON error: {e.Message}", e);
            }

            // [UploadTiming] Diagnostic: time spent receiving + staging the multipart body vs the finalize/commit step.
            var receiveSw = System.Diagnostics.Stopwatch.StartNew();

            section = await ReadNextMultipartSectionAsync(reader);
            logger.LogDebug("ReceiveFileStream: AddMetadata");
            await AddMetadataPartAsync(driveUploadService, section);

            await ReadUploadPartsAsync(reader, driveUploadService);

            logger.LogInformation(
                "[UploadTiming] Multipart receive + local staging complete; receiveMs:{receiveMs}",
                receiveSw.ElapsedMilliseconds);

            logger.LogDebug("ReceiveFileStream: FinalizeUploadAsync");
            var finalizeSw = System.Diagnostics.Stopwatch.StartNew();
            var status = await driveUploadService.FinalizeUploadAsync(WebOdinContext);
            logger.LogInformation(
                "[UploadTiming] Request totals: receiveMs:{receiveMs} finalizeMs:{finalizeMs} totalMs:{totalMs}",
                receiveSw.ElapsedMilliseconds - finalizeSw.ElapsedMilliseconds, finalizeSw.ElapsedMilliseconds,
                receiveSw.ElapsedMilliseconds);
            return status;
        }

        /// <summary>
        /// Receives the stream for a new thumbnail being added to an existing file
        /// </summary>
        protected async Task<UploadPayloadResult> ReceivePayloadStream()
        {
            logger.LogWarning("files/uploadpayload endpoint used.  auth-context: {authContext}", WebOdinContext.AuthContext);
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var writer = this.GetHttpFileSystemResolver().ResolvePayloadStreamWriter();

            try
            {
                var section = await ReadNextMultipartSectionAsync(reader);
                AssertIsPart(section, MultipartUploadParts.PayloadUploadInstructions);

                await writer.StartUpload(section!.Body, WebOdinContext);

                await ReadUploadPartsAsync(reader, writer);

                var status = await writer.FinalizeUpload(WebOdinContext);
                return status;
            }
            finally
            {
                try
                {
                    await writer.CleanupStagingFiles(WebOdinContext);
                }
                catch (Exception e)
                {
                    logger.LogError(e, " Failure during file cleanup");
                }
            }
        }

        /// <summary>
        /// Reads the next multipart section, translating a malformed or truncated request body into a
        /// 400 client error instead of a 500. <see cref="MultipartReader"/> throws
        /// <see cref="IOException"/> ("Unexpected end of Stream, the content may have already been read
        /// by another component.") when the body ends before the next boundary is found, which is the
        /// caller's fault. Genuine client disconnects (request aborted) are left to propagate so the
        /// exception pipeline treats them as cancellations rather than errors.
        /// </summary>
        private async Task<MultipartSection> ReadNextMultipartSectionAsync(MultipartReader reader)
        {
            try
            {
                return await reader.ReadNextSectionAsync(HttpContext.RequestAborted);
            }
            catch (IOException) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw new OdinClientException(
                    "Malformed or truncated multipart request body.",
                    OdinClientErrorCode.MissingUploadData);
            }
        }

        /// <summary>
        /// Adds the metadata part, which the writer checks before any payload is read. A refusal therefore leaves the
        /// rest of the body unread, so the response says the connection closes: Kestrel needn't drain the body first,
        /// and an HTTP/1.1 client that watches for a response while it sends stops sending (RFC 9112 9.6).
        /// </summary>
        private protected async Task AddMetadataPartAsync(FileSystemStreamWriterBase writer, MultipartSection section)
        {
            AssertIsPart(section, MultipartUploadParts.Metadata);
            try
            {
                await writer.AddMetadata(section!.Body, WebOdinContext);
            }
            catch
            {
                HttpContext.Response.Headers.Connection = "close";
                throw;
            }
        }

        private protected void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (section == null)
            {
                throw new OdinClientException($"Missing {expectedPart.ToString()} section");
            }

            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Part must be {Enum.GetName(expectedPart)}", OdinClientErrorCode.MissingUploadData);
            }
        }

        /// <summary>
        /// Reads the parts that follow an upload's instructions until the request ends: payloads, thumbnails, payloadHash
        /// parts and, for an update, the metadata. Every upload route reads them here, so a new part type reaches all of them.
        /// Other known parts are ignored, as before.
        /// </summary>
        private protected async Task ReadUploadPartsAsync(MultipartReader reader, IUploadPartWriter writer,
            Func<Stream, Task> addMetadata = null)
        {
            var section = await ReadNextMultipartSectionAsync(reader);
            while (null != section)
            {
                switch (GetPart(section, out var fileSection))
                {
                    case MultipartUploadParts.Metadata when addMetadata != null:
                        await addMetadata(section.Body);
                        break;

                    case MultipartUploadParts.Payload:
                        TenantPathManager.AssertValidPayloadKey(fileSection?.FileName);
                        await writer.AddPayload(fileSection!.FileName, section.ContentType, fileSection.FileStream, WebOdinContext);
                        break;

                    case MultipartUploadParts.Thumbnail:
                        AssertIsValidThumbnailSection(fileSection);
                        await writer.AddThumbnail(fileSection.FileName, section.ContentType, fileSection.FileStream, WebOdinContext);
                        break;

                    case MultipartUploadParts.PayloadHash:
                        // Named for its payload by its file name, like the payload part itself
                        await writer.PayloadHashes.ResolveAsync(fileSection?.FileName, section.Body);
                        break;
                }

                section = await ReadNextMultipartSectionAsync(reader);
            }

            writer.PayloadHashes.AssertNoneMissing();
        }

        /// <summary>
        /// The part's name, and its file section when it has a file name; Content-Disposition is parsed once
        /// </summary>
        private MultipartUploadParts GetPart(MultipartSection section, out FileMultipartSection fileSection)
        {
            fileSection = section.AsFileSection();
            var name = fileSection?.Name ?? GetSectionName(section.ContentDisposition);
            if (!Enum.TryParse<MultipartUploadParts>(name, true, out var part))
            {
                throw new OdinClientException("Section does not match a known MultipartSection", OdinClientErrorCode.InvalidUpload);
            }

            return part;
        }

        private static void AssertIsValidThumbnailSection(FileMultipartSection fileSection)
        {
            if (null == fileSection)
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            if (string.IsNullOrWhiteSpace(fileSection.FileName))
            {
                throw new OdinClientException(
                    "Thumbnails must include the thumbnailKey, which matches the key in the InstructionSet.UploadManifest.",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }
        }

        private protected static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private protected static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(' ');
            var element = elements.First(entry => entry.StartsWith("boundary="));
            var boundary = element.Substring("boundary=".Length);
            // Remove quotes
            if (boundary.Length >= 2 && boundary[0] == '"' &&
                boundary[^1] == '"')
            {
                boundary = boundary.Substring(1, boundary.Length - 2);
            }

            return boundary;
        }

        private string GetSectionName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            return cd.Name?.Trim('"');
        }
    }
}