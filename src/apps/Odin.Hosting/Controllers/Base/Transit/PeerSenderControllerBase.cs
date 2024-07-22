﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Directly sends a file to a peer identity w/o saving it locally
    /// </summary>
    /// <remarks>
    /// Note: In alpha, this is done by using a temporary transient drive 🤢
    /// </remarks>
    public abstract class PeerSenderControllerBase(IPeerOutgoingTransferService peerOutgoingTransferService, TenantSystemStorage tenantSystemStorage)
        : DriveUploadControllerBase
    {
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("files/send")]
        public async Task<TransitResult> SendFile()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var fileSystemWriter = this.GetHttpFileSystemResolver().ResolveFileSystemWriter();

            // Note: comparing this to a drive upload - 
            // We receive TransitInstructionSet from the client then
            // map it to a UploadInstructionSet using a hard-coded internal
            // drive to allow apps to send files directly

            // Post alpha we can consider a more direct operation w/ot he temp-transient-drive

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);

            //
            // re-map to transit instruction set.  this is the critical point of this feature
            //
            var uploadInstructionSet = await RemapTransitInstructionSet(section!.Body);

            OdinValidationUtils.AssertValidRecipientList(uploadInstructionSet.TransitOptions.Recipients, false);

            using var cn = tenantSystemStorage.CreateConnection();
            await fileSystemWriter.StartUpload(uploadInstructionSet, WebOdinContext, cn);

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await fileSystemWriter.AddMetadata(section!.Body, WebOdinContext, cn);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentType);
                    await fileSystemWriter.AddPayload(payloadKey, contentType, fileSection.FileStream, WebOdinContext, cn);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentType);
                    await fileSystemWriter.AddThumbnail(thumbnailUploadKey, contentType, fileSection.FileStream, WebOdinContext, cn);
                }

                section = await reader.ReadNextSectionAsync();
            }

            var uploadResult = await fileSystemWriter.FinalizeUpload(WebOdinContext, cn);

            //TODO: this should come from the transit system
            // We need to return the remote information instead of the local drive information
            return new TransitResult()
            {
                RemoteGlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = uploadResult.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = uploadInstructionSet.TransitOptions.RemoteTargetDrive
                },
                RecipientStatus = uploadResult.RecipientStatus
            };
        }


        /// <summary>
        /// Sends a Delete Linked File Request to recipients
        /// </summary>
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("files/senddeleterequest")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileByGlobalTransitIdRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertValidRecipientList(request?.Recipients ?? [], false);
            OdinValidationUtils.AssertNotNull(request!.GlobalTransitIdFileIdentifier, nameof(request.GlobalTransitIdFileIdentifier));
            OdinValidationUtils.AssertIsTrue(request.GlobalTransitIdFileIdentifier.TargetDrive.IsValid(), "Target Drive is invalid");
            OdinValidationUtils.AssertIsTrue(request.GlobalTransitIdFileIdentifier.GlobalTransitId != Guid.Empty,
                "GlobalTransitId is empty (cannot be Guid.Empty)");

            using var cn = tenantSystemStorage.CreateConnection();

            //send the deleted file
            var map = await peerOutgoingTransferService.SendDeleteFileRequest(request.GlobalTransitIdFileIdentifier,
                new FileTransferOptions()
                {
                    FileSystemType = request.FileSystemType,
                    TransferFileType = TransferFileType.Normal
                },
                request.Recipients, WebOdinContext, cn);

            return new JsonResult(map);
        }


        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("files/uploadpayload")]
        public async Task<UploadPayloadResult> UploadPayload()
        {
            // Rules:
            // Cannot upload encrypted payload to encrypted file (how can i tell?)
            // cannot upload encrypted payload to unecnrypted file (how can i tell?)
            

            throw new NotImplementedException("WIP");
            using var cn = tenantSystemStorage.CreateConnection();

            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var writer = this.GetHttpFileSystemResolver().ResolvePayloadStreamWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.PayloadUploadInstructions);

            var instructionSet = await RemapUploadInstructionSet(section!.Body);
            await writer.StartUpload(instructionSet, WebOdinContext, cn);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentType);
                    await writer.AddPayload(payloadKey, contentType, fileSection.FileStream, WebOdinContext, cn);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentType);
                    await writer.AddThumbnail(thumbnailUploadKey, contentType, fileSection.FileStream, WebOdinContext, cn);
                }

                section = await reader.ReadNextSectionAsync();
            }

            var status = await writer.FinalizeUpload(WebOdinContext, cn, this.GetHttpFileSystemResolver().GetFileSystemType());
            return status;
        }


        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("files/deletepayload")]
        public async Task<DeletePayloadResult> DeletePayload(PeerDeletePayloadRequest request)
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
            using var cn = tenantSystemStorage.CreateConnection();

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key, request.VersionTag.GetValueOrDefault(), WebOdinContext, cn)
            };
        }


        /// <summary>
        /// Map the client's transit instructions to an upload instruction set so we
        /// so we can keep the upload infrastructure for Alpha
        /// </summary>
        private async Task<UploadInstructionSet> RemapTransitInstructionSet(Stream transitInstructionStream)
        {
            string json = await new StreamReader(transitInstructionStream).ReadToEndAsync();
            var peerInstructionSet = OdinSystemSerializer.Deserialize<PeerDirectInstructionSet>(json);

            var uploadInstructionSet = new UploadInstructionSet()
            {
                TransferIv = peerInstructionSet.TransferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = SystemDriveConstants.TransientTempDrive,
                    OverwriteFileId = default,
                    StorageIntent = peerInstructionSet.StorageIntent
                },
                TransitOptions = new TransitOptions()
                {
                    IsTransient = true,
                    SendContents = SendContents.All,

                    //TODO: OMG HACK
                    OverrideRemoteGlobalTransitId = peerInstructionSet.OverwriteGlobalTransitFileId,

                    RemoteTargetDrive = peerInstructionSet.RemoteTargetDrive,
                    Recipients = peerInstructionSet.Recipients,
                },
                Manifest = peerInstructionSet.Manifest
            };

            return uploadInstructionSet;
        }

        private async Task<UploadPayloadInstructionSet> RemapUploadInstructionSet(Stream data)
        {
            string json = await new StreamReader(data).ReadToEndAsync();
            var originalInstructionSet = OdinSystemSerializer.Deserialize<PeerUploadPayloadInstructionSet>(json);

            var instructionSet = new UploadPayloadInstructionSet()
            {
                TargetFile = new ExternalFileIdentifier()
                {
                    //FileId = ?? 
                    TargetDrive = SystemDriveConstants.TransientTempDrive
                },
                Manifest = originalInstructionSet.Manifest,
                VersionTag = originalInstructionSet.VersionTag,
                Recipients = originalInstructionSet.Recipients
            };

            return instructionSet;
        }
    }
}