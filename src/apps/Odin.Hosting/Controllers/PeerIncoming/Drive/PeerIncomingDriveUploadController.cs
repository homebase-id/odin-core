using System.IO;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;
using Odin.Core.Storage.SQLite;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerIncomingDriveUploadController : PeerPerimeterDriveUploadControllerBase
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly PushNotificationService _pushNotificationService;
        private PeerDriveIncomingTransferService _incomingTransferService;
        private IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;

        /// <summary />
        public PeerIncomingDriveUploadController(DriveManager driveManager,
            TenantSystemStorage tenantSystemStorage, IMediator mediator, FileSystemResolver fileSystemResolver, PushNotificationService pushNotificationService,
            ILoggerFactory loggerFactory)
        {
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _pushNotificationService = pushNotificationService;
            _loggerFactory = loggerFactory;
        }

        /// <summary />
        [HttpPost("upload")]
        public async Task<PeerTransferResponse> ReceiveIncomingTransfer()
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

            _incomingTransferService = GetPeerDriveIncomingTransferService(_fileSystem);
            await _incomingTransferService.InitializeIncomingTransfer(transferInstructionSet, WebOdinContext, cn);

            //

            var metadata = await ProcessMetadataSection(await reader.ReadNextSectionAsync(), cn);

            //

            var shouldExpectPayload = transferInstructionSet.ContentsProvided.HasFlag(SendContents.Payload);
            if (shouldExpectPayload)
            {
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
            }


            return await _incomingTransferService.FinalizeTransfer(metadata, WebOdinContext, cn);
        }

        [HttpPost("deletelinkedfile")]
        public async Task<PeerTransferResponse> DeleteLinkedFile(DeleteRemoteFileRequest request)
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            var perimeterService = GetPeerDriveIncomingTransferService(fileSystem);
            using var cn = _tenantSystemStorage.CreateConnection();
            return await perimeterService.AcceptDeleteLinkedFileRequest(
                request.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                request.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                request.FileSystemType,
                WebOdinContext,
                cn);
        }

        [HttpPost("mark-file-read")]
        public async Task<PeerTransferResponse> MarkFileAsRead(MarkFileAsReadRequest request)
        {
            await AssertIsValidCaller();

            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            var perimeterService = GetPeerDriveIncomingTransferService(fileSystem);
            using var cn = _tenantSystemStorage.CreateConnection();

            return await perimeterService.MarkFileAsRead(
                request.GlobalTransitIdFileIdentifier.TargetDrive,
                request.GlobalTransitIdFileIdentifier.GlobalTransitId,
                request.FileSystemType,
                WebOdinContext,
                cn);
        }

        private async Task<EncryptedRecipientTransferInstructionSet> ProcessTransferInstructionSet(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.TransferKeyHeader);
            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferInstructionSet = OdinSystemSerializer.Deserialize<EncryptedRecipientTransferInstructionSet>(json);

            OdinValidationUtils.AssertNotNull(transferInstructionSet, nameof(transferInstructionSet));
            OdinValidationUtils.AssertIsTrue(transferInstructionSet.IsValid(), "Invalid data deserialized when creating the TransferInstructionSet");

            return transferInstructionSet;
        }

        private async Task<FileMetadata> ProcessMetadataSection(MultipartSection section, DatabaseConnection cn)
        {
            AssertIsPart(section, MultipartHostTransferParts.Metadata);

            //HACK: need to optimize this 
            var json = await new StreamReader(section.Body).ReadToEndAsync();
            var metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);
            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await _incomingTransferService.AcceptMetadata("metadata", metadataStream, WebOdinContext, cn);
            return metadata;
        }

        private async Task ProcessPayloadSection(MultipartSection section, FileMetadata fileMetadata, DatabaseConnection cn)
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

        private async Task ProcessThumbnailSection(MultipartSection section, FileMetadata fileMetadata, DatabaseConnection cn)
        {
            AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey);

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

        private PeerDriveIncomingTransferService GetPeerDriveIncomingTransferService(IDriveFileSystem fileSystem)
        {
            return new PeerDriveIncomingTransferService(
                _loggerFactory.CreateLogger<PeerDriveIncomingTransferService>(),
                _driveManager,
                fileSystem,
                _tenantSystemStorage,
                _mediator,
                _fileSystemResolver,
                _pushNotificationService);
        }
    }
}