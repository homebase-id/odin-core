using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit.Payload
{
    /// <summary>
    /// Directly sends a file to a peer identity w/o saving it locally
    /// </summary>
    /// <remarks>
    /// Note: In alpha, this is done by using a temporary transient drive 🤢
    /// </remarks>
    public abstract class PeerDirectPayloadControllerBase(
        IPeerOutgoingTransferService peerOutgoingTransferService,
        TenantSystemStorage tenantSystemStorage,
        DriveManager driveManager)
        : DriveUploadControllerBase
    {
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("files/uploadpayload")]
        public async Task<PeerUploadPayloadResult> UploadPayload()
        {
            // Rules:
            // Cannot upload encrypted payload to encrypted file (how can I tell?)
            // cannot upload encrypted payload to unencrypted file (how can I tell?)

            using var cn = tenantSystemStorage.CreateConnection();

            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var writer = new PeerDirectPayloadStreamWriter(peerOutgoingTransferService, driveManager, this.GetHttpFileSystemResolver().ResolveFileSystem());

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.PayloadUploadInstructions);

            string json = await new StreamReader(section!.Body).ReadToEndAsync();
            var instructionSet = OdinSystemSerializer.Deserialize<PeerDirectUploadPayloadInstructionSet>(json);
            instructionSet.AssertIsValid();

            await writer.StartUpload(instructionSet, WebOdinContext, cn);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentType);
                    await writer.AddPayload(payloadKey, fileSection.FileStream, contentType, WebOdinContext, cn);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentType);
                    await writer.AddThumbnail(thumbnailUploadKey, fileSection.FileStream, contentType, WebOdinContext, cn);
                }

                section = await reader.ReadNextSectionAsync();
            }

            var status = await writer.FinalizeUpload(WebOdinContext, cn, this.GetHttpFileSystemResolver().GetFileSystemType());
            return status;
        }

        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("files/deletepayload")]
        public async Task<PeerDeletePayloadResult> DeletePayload(PeerDeletePayloadRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, "request");
            OdinValidationUtils.AssertNotEmptyGuid(request.VersionTag, nameof(request.VersionTag), OdinClientErrorCode.MissingVersionTag);
            OdinValidationUtils.AssertValidRecipientList(request.Recipients, false);
            DriveFileUtility.AssertValidPayloadKey(request.Key);

            request.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

            using var cn = tenantSystemStorage.CreateConnection();

            return new PeerDeletePayloadResult()
            {
                RecipientStatus = await peerOutgoingTransferService.DeletePayload(request.File, request.VersionTag, request.Key, request.Recipients,
                    this.GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn)
            };
        }
    }
}