﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerIncomingDriveUpdateController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly PushNotificationService _pushNotificationService;
        private PeerDriveIncomingTransferService _incomingTransferService;
        private IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;
        private Guid _stateItemId;

        /// <summary />
        public PeerIncomingDriveUpdateController(OdinContextAccessor contextAccessor, DriveManager driveManager,
            TenantSystemStorage tenantSystemStorage, IMediator mediator, FileSystemResolver fileSystemResolver, PushNotificationService pushNotificationService)
        {
            _contextAccessor = contextAccessor;
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _pushNotificationService = pushNotificationService;
        }

        /// <summary />
        [HttpPost("upload")]
        public async Task<PeerTransferResponse> ReceiveIncomingTransfer()
        {
            await ValidateCaller();

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
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);
            _fileSystem.Storage.AssertCanWriteToDrive(driveId);
            //End Optimizations

            _incomingTransferService = GetPerimeterService(_fileSystem);
            _stateItemId = await _incomingTransferService.InitializeIncomingTransfer(transferInstructionSet);

            //

            var metadata = await ProcessMetadataSection(await reader.ReadNextSectionAsync());

            //

            var shouldExpectPayload = transferInstructionSet.ContentsProvided.HasFlag(SendContents.Payload);
            if (shouldExpectPayload)
            {
                var section = await reader.ReadNextSectionAsync();
                while (null != section)
                {
                    if (IsPayloadPart(section))
                    {
                        await ProcessPayloadSection(section, metadata);
                    }

                    if (IsThumbnail(section))
                    {
                        await ProcessThumbnailSection(section, metadata);
                    }

                    section = await reader.ReadNextSectionAsync();
                }
            }

            //

            return await _incomingTransferService.FinalizeTransfer(this._stateItemId, metadata);
        }

        [HttpPost("deletelinkedfile")]
        public async Task<PeerTransferResponse> DeleteLinkedFile(DeleteRemoteFileRequest request)
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            var perimeterService = GetPerimeterService(fileSystem);
            return await perimeterService.AcceptDeleteLinkedFileRequest(
                request.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                request.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                request.FileSystemType);
        }

        private Task ValidateCaller()
        {
            //TODO: later add check to see if this is from an introduction?

            var dotYouContext = _contextAccessor.GetCurrent();
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
            OdinValidationUtils.AssertIsTrue(transferInstructionSet.IsValid(), "Invalid data deserialized when creating the TransferInstructionSet");

            return transferInstructionSet;
        }

        private async Task<FileMetadata> ProcessMetadataSection(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.Metadata);

            //HACK: need to optimize this 
            var json = await new StreamReader(section.Body).ReadToEndAsync();
            var metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);
            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await _incomingTransferService.AcceptPart(this._stateItemId, MultipartHostTransferParts.Metadata, "metadata", metadataStream);
            return metadata;
        }

        private async Task ProcessPayloadSection(MultipartSection section, FileMetadata fileMetadata)
        {
            AssertIsPayloadPart(section, out var fileSection, out var payloadKey);

            // Validate the payload key is defined in the set being sent
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                throw new OdinClientException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = DriveFileUtility.GetPayloadFileExtension(payloadKey, payloadDescriptor.Uid);
            await _incomingTransferService.AcceptPart(this._stateItemId, MultipartHostTransferParts.Payload, extension, fileSection.FileStream);
        }

        private async Task ProcessThumbnailSection(MultipartSection section, FileMetadata fileMetadata)
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
            await _incomingTransferService.AcceptPart(this._stateItemId, MultipartHostTransferParts.Thumbnail, extension, fileSection.FileStream);
        }

        private void AssertIsPayloadPart(MultipartSection section, out FileMultipartSection fileSection, out string payloadKey)
        {
            var expectedPart = MultipartHostTransferParts.Payload;
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Payloads have name of {Enum.GetName(expectedPart)}", OdinClientErrorCode.InvalidPayloadNameOrKey);
            }

            fileSection = section.AsFileSection();
            DriveFileUtility.AssertValidPayloadKey(fileSection?.FileName);
            payloadKey = fileSection?.FileName;
        }

        private void AssertIsValidThumbnailPart(MultipartSection section, out FileMultipartSection fileSection,
            out string thumbnailUploadKey, out string contentType)
        {
            var expectedPart = MultipartHostTransferParts.Thumbnail;
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Thumbnails have name of {Enum.GetName(expectedPart)}", OdinClientErrorCode.InvalidThumnbnailName);
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
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
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

            throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
        }

        private PeerDriveIncomingTransferService GetPerimeterService(IDriveFileSystem fileSystem)
        {
            return new PeerDriveIncomingTransferService(_contextAccessor,
                _driveManager,
                fileSystem,
                _tenantSystemStorage,
                _mediator,
                _fileSystemResolver,
                _pushNotificationService);
        }
    }
}