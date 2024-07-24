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
        public async Task<UploadPayloadResult> UploadPayload()
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
            instructionSet.Manifest.AssertIsValid();

            await writer.StartUpload(instructionSet, WebOdinContext, cn);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey);
                    await writer.AddPayload(payloadKey, fileSection.FileStream, WebOdinContext, cn);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey);
                    await writer.AddThumbnail(thumbnailUploadKey, fileSection.FileStream, WebOdinContext, cn);
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
    }
}