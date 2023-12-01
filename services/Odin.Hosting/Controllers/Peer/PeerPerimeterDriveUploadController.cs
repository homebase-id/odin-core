using System;
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
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.FileSystem.Comment;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Core.Services.Peer.ReceivingHost.Quarantine;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Controllers.Peer
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterDriveUploadController : ControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly NotificationDataService _notificationDataService;
        private ITransitPerimeterService _perimeterService;
        private IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;
        private Guid _stateItemId;

        /// <summary />
        public PeerPerimeterDriveUploadController(OdinContextAccessor contextAccessor, DriveManager driveManager,
            TenantSystemStorage tenantSystemStorage, IMediator mediator, FileSystemResolver fileSystemResolver, NotificationDataService notificationDataService)
        {
            _contextAccessor = contextAccessor;
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
            _mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
            _notificationDataService = notificationDataService;
        }

        /// <summary />
        [HttpPost("upload")]
        public async Task<HostTransitResponse> AcceptHostToHostTransfer()
        {
            try
            {
                if (!IsMultipartContentType(HttpContext.Request.ContentType))
                {
                    throw new HostToHostTransferException("Data is not multi-part content");
                }

                var boundary = GetBoundary(HttpContext.Request.ContentType);
                var reader = new MultipartReader(boundary, HttpContext.Request.Body);

                var transferInstructionSet = await ProcessTransferInstructionSet(await reader.ReadNextSectionAsync());

                //Optimizations - the caller can't write to the drive, no need to accept any more of the file

                //S0100
                _fileSystem = ResolveFileSystem(transferInstructionSet.FileSystemType);

                //S1000, S2000 - can the sender write the content to the target drive?
                var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);
                _fileSystem.Storage.AssertCanWriteToDrive(driveId);

                //End Optimizations

                _perimeterService = new TransitPerimeterService(_contextAccessor,
                    _driveManager, _fileSystem, _tenantSystemStorage, _mediator, _fileSystemResolver, _notificationDataService);

                _stateItemId = await _perimeterService.InitializeIncomingTransfer(transferInstructionSet);

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

                if (!await _perimeterService.IsFileValid(_stateItemId))
                {
                    throw new HostToHostTransferException("Transfer does not contain all required parts.");
                }

                //TODO: that metadata should be on the state item.  hacked in place while figuring out direct-write support
                var result = await _perimeterService.FinalizeTransfer(this._stateItemId, metadata);
                if (result.Code == TransitResponseCode.Rejected)
                {
                    HttpContext.Abort(); //TODO:does this abort also kill the response?
                    throw new HostToHostTransferException("Transmission Aborted");
                }

                return result;
            }
            catch (OdinSecurityException)
            {
                //TODO: break down the actual errors so we can send to the
                //caller information about why it was rejected w/o giving away
                //sensitive stuff
                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.AccessDenied,
                    Message = "Access Denied"
                };
            }
            catch (Exception)
            {
                //TODO: break down the actual errors so we can send to the
                //caller information about why it was rejected w/o giving away
                //sensitive stuff
                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.Rejected,
                    Message = "Error"
                };
            }
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
            var transferKeyHeader = OdinSystemSerializer.Deserialize<EncryptedRecipientTransferInstructionSet>(json);
            return transferKeyHeader;
        }

        private async Task<FileMetadata> ProcessMetadataSection(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.Metadata);

            //HACK: need to optimize this 
            var json = await new StreamReader(section.Body).ReadToEndAsync();
            var metadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);
            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            //TODO: determine if the filter needs to decide if its result should be sent back to the sender
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Metadata, "metadata", metadataStream);
            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }


            return metadata;
        }

        private async Task ProcessPayloadSection(MultipartSection section, FileMetadata fileMetadata)
        {
            AssertIsPayloadPart(section, out var fileSection, out var payloadKey);

            // Validate the payload key is defined in the set being sent
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                throw new HostToHostTransferException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = DriveFileUtility.GetPayloadFileExtension(payloadKey);

            //TODO: determine if the filter needs to decide if its result should be sent back to the sender
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Payload, extension,
                fileSection.FileStream);

            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
        }

        private async Task ProcessThumbnailSection(MultipartSection section, FileMetadata fileMetadata)
        {
            AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentType);

            var parts = thumbnailUploadKey.Split(DriveFileUtility.TransitThumbnailKeyDelimiter);
            if (parts.Length != 3)
            {
                throw new HostToHostTransferException($"The thumbnail upload key provided is invalid {thumbnailUploadKey}");
            }

            var payloadKey = parts[0];
            var width = int.Parse(parts[1]);
            var height = int.Parse(parts[2]);
            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var payloadDescriptor = fileMetadata.GetPayloadDescriptor(payloadKey);

            if (null == payloadDescriptor)
            {
                throw new HostToHostTransferException($"Payload sent with key that is not defined in the metadata header: {payloadKey}");
            }

            string extension = DriveFileUtility.GetThumbnailFileExtension(width, height, payloadKey);

            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Thumbnail, extension,
                fileSection.FileStream);

            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
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

        private protected void AssertIsValidThumbnailPart(MultipartSection section, out FileMultipartSection fileSection,
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

        private void AssertIsValidThumbnailPart_old(MultipartSection section, out FileMultipartSection fileSection,
            out int width, out int height)
        {
            var expectedPart = MultipartHostTransferParts.Thumbnail;

            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Thumbnails must have name of {Enum.GetName(expectedPart)}", OdinClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            string[] parts = fileSection.FileName.Split('x');
            if (!Int32.TryParse(parts[0], out width) || !Int32.TryParse(parts[1], out height))
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }
        }

        private void AssertIsPart(MultipartSection section, MultipartHostTransferParts expectedPart)
        {
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new HostToHostTransferException($"Part must be {Enum.GetName(expectedPart)}");
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
    }
}