using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class PeerIncomingDriveUploadController : OdinControllerBase
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly PeerOutbox _peerOutbox;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly OdinConfiguration _odinConfiguration;
        private readonly ILogger<PeerIncomingDriveUploadController> _logger;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly FeedWriter _feedWriter;
        private readonly IDriveManager _driveManager;

        private readonly FileSystemResolver _fileSystemResolver;
        private readonly PushNotificationService _pushNotificationService;
        private PeerDriveIncomingTransferService _incomingTransferService;
        private IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;

        /// <summary />
        public PeerIncomingDriveUploadController(IDriveManager driveManager,
            IMediator mediator,
            FileSystemResolver fileSystemResolver,
            PushNotificationService pushNotificationService,
            ILoggerFactory loggerFactory,
            PeerOutbox peerOutbox,
            IOdinHttpClientFactory odinHttpClientFactory,
            CircleNetworkService circleNetworkService,
            OdinConfiguration odinConfiguration,
            ILogger<PeerIncomingDriveUploadController> logger,
            TransitInboxBoxStorage transitInboxBoxStorage,
            FeedWriter feedWriter)
        {
            _driveManager = driveManager;

            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _pushNotificationService = pushNotificationService;
            _loggerFactory = loggerFactory;
            _peerOutbox = peerOutbox;
            _odinHttpClientFactory = odinHttpClientFactory;
            _circleNetworkService = circleNetworkService;
            _odinConfiguration = odinConfiguration;
            _logger = logger;
            _transitInboxBoxStorage = transitInboxBoxStorage;
            _feedWriter = feedWriter;
        }

        /// <summary />
        [HttpPost("upload")]
        public async Task<PeerTransferResponse> ReceiveIncomingTransfer()
        {
            List<PayloadDescriptor> uploadedPayloads = new();
            try
            {
                if (WebOdinContext.AuthContext != "AutomatedIdentityAuthScheme")
                {
                    WebOdinContext.Caller.AssertCallerIsConnected();
                }

                if (!IsMultipartContentType(HttpContext.Request.ContentType))
                {
                    throw new OdinClientException("Data is not multi-part content");
                }

                var boundary = GetBoundary(HttpContext.Request.ContentType);
                var reader = new MultipartReader(boundary, HttpContext.Request.Body);

                var transferInstructionSet = await ProcessTransferInstructionSet(await reader.ReadNextSectionAsync());

                OdinValidationUtils.AssertNotNull(transferInstructionSet, nameof(transferInstructionSet));
                OdinValidationUtils.AssertIsTrue(transferInstructionSet.IsValid(),
                    "Invalid data deserialized when creating the TransferInstructionSet");

                //Optimizations - the caller can't write to the drive, no need to accept any more of the file

                //S0100
                _fileSystem = ResolveFileSystem(transferInstructionSet.FileSystemType);

                //S1000, S2000 - can the sender write the content to the target drive?
                var driveId = transferInstructionSet.DriveId;
                await _fileSystem.Storage.AssertCanWriteToDrive(driveId, WebOdinContext);
                var drive = await _driveManager.GetDriveAsync(driveId);
                if ((transferInstructionSet.AppNotificationOptions?.Recipients?.Any() ?? false) && !drive.AllowSubscriptions)
                {
                    throw new OdinSecurityException("Attempt to distribute app notifications to drive which does not allow subscriptions");
                }

                //End Optimizations

                var metadata = await Initialize(transferInstructionSet, reader);

                metadata.Validate(WebOdinContext.Tenant);

                //
                var shouldExpectPayload = !metadata.PayloadsAreRemote;
                if (shouldExpectPayload)
                {
                    var section = await reader.ReadNextSectionAsync();
                    while (null != section)
                    {
                        if (IsPayloadPart(section))
                        {
                            var descriptor = await ProcessPayloadSection(section, metadata);
                            uploadedPayloads.Add(descriptor);
                        }

                        if (IsThumbnail(section))
                        {
                            await ProcessThumbnailSection(section, metadata);
                        }

                        section = await reader.ReadNextSectionAsync();
                    }
                }

                return await _incomingTransferService.FinalizeTransfer(metadata, WebOdinContext);
            }
            finally
            {
                try
                {
                    if (null != _incomingTransferService)
                    {
                        await _incomingTransferService.CleanupTempFiles(uploadedPayloads, WebOdinContext);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error during file cleanup");
                }
            }
        }

        private async Task<FileMetadata> Initialize(
            EncryptedRecipientTransferInstructionSet transferInstructionSet, MultipartReader reader)
        {
            var metadataSection = await reader.ReadNextSectionAsync();
            AssertIsPart(metadataSection, MultipartHostTransferParts.Metadata);
            var json = await new StreamReader(metadataSection!.Body).ReadToEndAsync();
            var metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);

            _incomingTransferService = GetPerimeterService(_fileSystem);
            await _incomingTransferService.InitializeIncomingTransfer(transferInstructionSet,
                metadata, WebOdinContext);

            return metadata;
        }

        [HttpPost("deletelinkedfile")]
        public async Task<PeerTransferResponse> DeleteLinkedFile(DeleteRemoteFileRequest request)
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            var perimeterService = GetPerimeterService(fileSystem);

            return await perimeterService.AcceptDeleteLinkedFileRequestAsync(
                request.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                request.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                request.FileSystemType,
                WebOdinContext);
        }

        [HttpPost("mark-file-read")]
        public async Task<PeerTransferResponse> MarkFileAsRead(MarkFileAsReadRequest request)
        {
            await AssertIsValidCaller();

            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            var perimeterService = GetPerimeterService(fileSystem);


            return await perimeterService.MarkFileAsReadAsync(
                request.GlobalTransitIdFileIdentifier.TargetDrive,
                request.GlobalTransitIdFileIdentifier.GlobalTransitId,
                request.FileSystemType,
                WebOdinContext);
        }

        private Task AssertIsValidCaller()
        {
            //TODO: later add check to see if this is from an introduction?

            var dotYouContext = WebOdinContext;
            var isValidCaller = dotYouContext.Caller.IsConnected || dotYouContext.Caller.ClientTokenType == ClientTokenType.DataProvider;
            if (!isValidCaller)
            {
                throw new OdinSecurityException("Caller must be connected");
            }

            return Task.CompletedTask;
        }

        private bool IsPayloadPart(MultipartSection section)
        {
            if (section == null)
            {
                return false;
            }

            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part))
            {
                throw new OdinClientException("Section does not match a known MultipartSection", OdinClientErrorCode.InvalidUpload);
            }

            return part == MultipartHostTransferParts.Payload;
        }

        private bool IsThumbnail(MultipartSection section)
        {
            if (section == null)
            {
                return false;
            }

            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part))
            {
                throw new OdinClientException("Section does not match a known MultipartSection", OdinClientErrorCode.InvalidUpload);
            }

            return part == MultipartHostTransferParts.Thumbnail;
        }

        private async Task<EncryptedRecipientTransferInstructionSet> ProcessTransferInstructionSet(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.TransferKeyHeader);
            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferInstructionSet = OdinSystemSerializer.Deserialize<EncryptedRecipientTransferInstructionSet>(json);

            OdinValidationUtils.AssertNotNull(transferInstructionSet, nameof(transferInstructionSet));
            OdinValidationUtils.AssertIsTrue(transferInstructionSet.IsValid(),
                "Invalid data deserialized when creating the TransferInstructionSet");

            return transferInstructionSet;
        }

        private async Task<PayloadDescriptor> ProcessPayloadSection(MultipartSection section, FileMetadata fileMetadata)
        {
            AssertIsPayloadPart(section, out var fileSection, out var payloadKey);

            // Validate the payload key is defined in the set being sent
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = TenantPathManager.GetBasePayloadFileNameAndExtension(payloadKey, payloadDescriptor.Uid);
            await _incomingTransferService.AcceptPayload(payloadKey, extension, fileSection.FileStream, WebOdinContext);
            return payloadDescriptor;
        }

        private async Task ProcessThumbnailSection(MultipartSection section, FileMetadata fileMetadata)
        {
            AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out _);

            var parts = thumbnailUploadKey.Split(TenantPathManager.TransitThumbnailKeyDelimiter);
            if (parts.Length != 3)
            {
                throw new OdinClientException($"The thumbnail upload key provided is invalid {thumbnailUploadKey}");
            }

            var payloadKey = parts[0];
            var width = int.Parse(parts[1]);
            var height = int.Parse(parts[2]);
            TenantPathManager.AssertValidPayloadKey(payloadKey);
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);

            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = TenantPathManager.GetThumbnailFileNameAndExtension(payloadKey, payloadDescriptor.Uid, width, height);
            await _incomingTransferService.AcceptThumbnail(payloadKey, thumbnailUploadKey, extension,
                fileSection.FileStream,
                WebOdinContext);
        }

        private void AssertIsPayloadPart(MultipartSection section, out FileMultipartSection fileSection, out string payloadKey)
        {
            var expectedPart = MultipartHostTransferParts.Payload;
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Payloads have name of {Enum.GetName(expectedPart)}",
                    OdinClientErrorCode.InvalidPayloadNameOrKey);
            }

            fileSection = section.AsFileSection();
            TenantPathManager.AssertValidPayloadKey(fileSection?.FileName);
            payloadKey = fileSection?.FileName;
        }

        private void AssertIsValidThumbnailPart(MultipartSection section, out FileMultipartSection fileSection,
            out string thumbnailUploadKey, out string contentType)
        {
            var expectedPart = MultipartHostTransferParts.Thumbnail;
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Thumbnails have name of {Enum.GetName(expectedPart)}",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();

            contentType = section.ContentType;
            if (string.IsNullOrEmpty(contentType) || string.IsNullOrWhiteSpace(contentType))
            {
                throw new OdinClientException(
                    "Thumbnails must include a valid contentType in the multi-part upload.",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            thumbnailUploadKey = fileSection?.FileName;
            if (string.IsNullOrEmpty(thumbnailUploadKey) || string.IsNullOrWhiteSpace(thumbnailUploadKey))
            {
                throw new OdinClientException(
                    "Thumbnails must include the thumbnailKey, which matches the key in the InstructionSet.UploadManifest.",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }
        }

        private void AssertIsPart(MultipartSection section, MultipartHostTransferParts expectedPart)
        {
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Part must be {Enum.GetName(expectedPart)}");
            }
        }

        private static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(' ');
            var element = elements.First(entry => entry.StartsWith("boundary="));
            var boundary = element.Substring("boundary=".Length);
            // Remove quotes
            if (boundary.Length >= 2 && boundary[0] == '"' &&
                boundary[boundary.Length - 1] == '"')
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

        private IDriveFileSystem ResolveFileSystem(FileSystemType fileSystemType)
        {
            //TODO: this is duplicated code

            var ctx = this.HttpContext;

            if (fileSystemType == FileSystemType.Standard)
            {
                return ctx!.RequestServices.GetRequiredService<StandardFileSystem>();
            }

            if (fileSystemType == FileSystemType.Comment)
            {
                return ctx!.RequestServices.GetRequiredService<CommentFileSystem>();
            }

            throw new OdinClientException("Invalid file system type or could not parse instruction set",
                OdinClientErrorCode.InvalidFileSystemType);
        }

        private PeerDriveIncomingTransferService GetPerimeterService(IDriveFileSystem fileSystem)
        {
            // SEB:TODO DI this?
            return new PeerDriveIncomingTransferService(
                _loggerFactory.CreateLogger<PeerDriveIncomingTransferService>(),
                _driveManager,
                fileSystem,
                _mediator,
                _pushNotificationService,
                _peerOutbox,
                _odinHttpClientFactory,
                _circleNetworkService,
                _fileSystemResolver,
                _odinConfiguration,
                _transitInboxBoxStorage,
                _feedWriter);
        }
    }
}