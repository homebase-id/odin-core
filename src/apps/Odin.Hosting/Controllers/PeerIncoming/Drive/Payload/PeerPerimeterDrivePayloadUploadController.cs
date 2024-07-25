using System.IO;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Microsoft.Extensions.Logging;
using ILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;


namespace Odin.Hosting.Controllers.PeerIncoming.Drive.Payload
{
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterDrivePayloadUploadController : PeerPerimeterDriveUploadControllerBase
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private PeerDriveIncomingPayloadCollectorService _incomingTransferService;
        private IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;
        private readonly ILoggerFactory _loggerFactory;
        private readonly PushNotificationService _pushNotificationService;

        /// <summary />
        public PeerPerimeterDrivePayloadUploadController(
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator, 
            PushNotificationService pushNotificationService,
            ILoggerFactory loggerFactory)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _mediator = mediator;
            _pushNotificationService = pushNotificationService;
            _loggerFactory = loggerFactory;
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

            //Optimizations - the caller can't write to the drive, no need to accept any more of the file

            //S0100
            _fileSystem = ResolveFileSystem(transferInstructionSet.FileSystemType);

            //S1000, S2000 - can the sender write the content to the target drive?
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(transferInstructionSet.TargetFile.Drive);
            using var cn = _tenantSystemStorage.CreateConnection();
            await _fileSystem.Storage.AssertCanWriteToDrive(driveId, WebOdinContext, cn);
            //End Optimizations

            _incomingTransferService = GetPeerDriveIncomingTransferService(_fileSystem);
            await _incomingTransferService.InitializeIncomingTransfer(transferInstructionSet, WebOdinContext, cn);

            //

            var section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    await ProcessPayloadSection(section, transferInstructionSet.Manifest, cn);
                }

                if (IsThumbnail(section))
                {
                    await ProcessThumbnailSection(section, transferInstructionSet.Manifest, cn);
                }

                section = await reader.ReadNextSectionAsync();
            }

            return await _incomingTransferService.FinalizeTransfer(WebOdinContext, cn);
        }

        private async Task<PayloadTransferInstructionSet> ProcessTransferInstructionSet(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.PayloadTransferInstructionSet);
            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferInstructionSet = OdinSystemSerializer.Deserialize<PayloadTransferInstructionSet>(json);

            OdinValidationUtils.AssertNotNull(transferInstructionSet, nameof(transferInstructionSet));
            transferInstructionSet.AssertIsValid();

            return transferInstructionSet;
        }

        private async Task ProcessPayloadSection(MultipartSection section, UploadManifest manifest, DatabaseConnection cn)
        {
            AssertIsPayloadPart(section, out var fileSection, out var payloadKey);

            // Validate the payload key is defined in the set being sent
            var payloadDescriptor = manifest.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }
            
            string extension = DriveFileUtility.GetPayloadFileExtension(payloadKey, payloadDescriptor.PayloadUid);
            await _incomingTransferService.AcceptPayload(payloadKey, extension, fileSection.FileStream, WebOdinContext, cn);
        }

        private async Task ProcessThumbnailSection(MultipartSection section, UploadManifest manifest, DatabaseConnection cn)
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
            var payloadDescriptor = manifest.GetPayloadDescriptor(payloadKey);

            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadDescriptor.PayloadUid, width, height);
            await _incomingTransferService.AcceptThumbnail(payloadKey, thumbnailUploadKey, extension,
                fileSection.FileStream,
                WebOdinContext, cn);
        }

        private PeerDriveIncomingPayloadCollectorService GetPeerDriveIncomingTransferService(IDriveFileSystem fileSystem)
        {
            return new PeerDriveIncomingPayloadCollectorService(
                _loggerFactory.CreateLogger<PeerDriveIncomingPayloadCollectorService>(),
                fileSystem,
                _tenantSystemStorage,
                _mediator,
                _pushNotificationService);
        }
    }
}