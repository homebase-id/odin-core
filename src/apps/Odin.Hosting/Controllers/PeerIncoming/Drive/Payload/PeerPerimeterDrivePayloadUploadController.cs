using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive.Payload
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterDrivePayloadUploadController : PeerPerimeterDriveUploadControllerBase
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private PeerDriveIncomingTransferService _incomingTransferService;
        private IDriveFileSystem _fileSystem;

        /// <summary />
        public PeerPerimeterDrivePayloadUploadController(FileSystemResolver fileSystemResolver, TenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _fileSystemResolver = fileSystemResolver;
        }

        /// <summary />
        [HttpPost("update-payloads")]
        public async Task<PeerTransferResponse> ReceivePayloadUpdates()
        {
            await AssertIsValidCaller();

            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var transferInstructionSet = await ProcessTransferInstructionSet(await reader.ReadNextSectionAsync());

            OdinValidationUtils.AssertNotNull(transferInstructionSet, nameof(transferInstructionSet));
            OdinValidationUtils.AssertIsTrue(transferInstructionSet.IsValid(), "Invalid data deserialized when creating the TransferInstructionSet");

            //Optimizations - the caller can't write to the drive, no need to accept any more of the file

            //S0100
            _fileSystem = ResolveFileSystem(transferInstructionSet.FileSystemType);

            //S1000, S2000 - can the sender write the content to the target drive?
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);
            using var cn = _tenantSystemStorage.CreateConnection();
            await _fileSystem.Storage.AssertCanWriteToDrive(driveId, WebOdinContext, cn);
            //End Optimizations

            _incomingTransferService = GetPerimeterService(_fileSystem);
            await _incomingTransferService.InitializeIncomingTransfer(transferInstructionSet, WebOdinContext, cn);

            //

            var metadata = await ProcessManifestSection(await reader.ReadNextSectionAsync(), cn);

            //

            var section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    await ProcessPayloadSection(section, metadata, cn);
                }

                if (IsThumbnail(section))
                {
                    await ProcessThumbnailSection(section, metadata, cn);
                }

                section = await reader.ReadNextSectionAsync();
            }

            return await _incomingTransferService.FinalizeTransfer(metadata, WebOdinContext, cn);
        }

        protected async Task<EncryptedRecipientTransferInstructionSet> ProcessTransferInstructionSet(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.TransferKeyHeader);
            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferInstructionSet = OdinSystemSerializer.Deserialize<EncryptedRecipientTransferInstructionSet>(json);

            OdinValidationUtils.AssertNotNull(transferInstructionSet, nameof(transferInstructionSet));
            OdinValidationUtils.AssertIsTrue(transferInstructionSet.IsValid(), "Invalid data deserialized when creating the TransferInstructionSet");

            return transferInstructionSet;
        }

        protected async Task ProcessPayloadSection(MultipartSection section, FileMetadata fileMetadata, DatabaseConnection cn)
        {
            AssertIsPayloadPart(section, out var fileSection, out var payloadKey);

            // Validate the payload key is defined in the set being sent
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = DriveFileUtility.GetPayloadFileExtension(payloadKey, payloadDescriptor.Uid);
            await _incomingTransferService.AcceptPayload(payloadKey, extension, fileSection.FileStream, WebOdinContext,
                cn);
        }

        protected async Task ProcessThumbnailSection(MultipartSection section, FileMetadata fileMetadata, DatabaseConnection cn)
        {
            AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out _);

            var parts = thumbnailUploadKey.Split(DriveFileUtility.TransitThumbnailKeyDelimiter);
            if (parts.Length != 3)
            {
                throw new OdinClientException($"The thumbnail upload key provided is invalid {thumbnailUploadKey}");
            }

            var payloadKey = parts[0];
            var width = int.Parse(parts[1]);
            var height = int.Parse(parts[2]);
            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);

            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadDescriptor.Uid, width, height);
            await _incomingTransferService.AcceptThumbnail(payloadKey, thumbnailUploadKey, extension,
                fileSection.FileStream,
                WebOdinContext, cn);
        }
    }
}