using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Core.Storage;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.TransitCertificateAuthScheme)]
    public class TransitPerimeterController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IPublicKeyService _publicKeyService;
        private readonly DriveManager _driveManager;
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private ITransitPerimeterService _perimeterService;
        private IDriveFileSystem _fileSystem;
        private readonly IMediator _mediator;
        private Guid _stateItemId;

        /// <summary />
        public TransitPerimeterController(DotYouContextAccessor contextAccessor, IPublicKeyService publicKeyService, DriveManager driveManager, ITenantSystemStorage tenantSystemStorage, IMediator mediator)
        {
            _contextAccessor = contextAccessor;
            _publicKeyService = publicKeyService;
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
            _mediator = mediator;
        }

        /// <summary />
        [HttpPost("stream")]
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

                var transferKeyHeader = await ProcessTransferKeyHeader(await reader.ReadNextSectionAsync());

                _fileSystem = ResolveFileSystem(transferKeyHeader.FileSystemType);
                _perimeterService = new TransitPerimeterService(_contextAccessor, _publicKeyService, _driveManager, _fileSystem, _tenantSystemStorage, _mediator);

                _stateItemId = await _perimeterService.InitializeIncomingTransfer(transferKeyHeader);
                //

                await ProcessMetadataSection(await reader.ReadNextSectionAsync());

                //

                await ProcessPayloadSection(await reader.ReadNextSectionAsync());

                //

                var section = await reader.ReadNextSectionAsync();
                while (null != section)
                {
                    await ProcessThumbnailSection(section);
                    section = await reader.ReadNextSectionAsync();
                }

                if (!await _perimeterService.IsFileValid(_stateItemId))
                {
                    throw new HostToHostTransferException("Transfer does not contain all required parts.");
                }

                var result = await _perimeterService.FinalizeTransfer(this._stateItemId);
                if (result.Code == TransitResponseCode.Rejected)
                {
                    HttpContext.Abort(); //TODO:does this abort also kill the response?
                    throw new HostToHostTransferException("Transmission Aborted");
                }

                return result;
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

        private async Task<RsaEncryptedRecipientTransferInstructionSet> ProcessTransferKeyHeader(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.TransferKeyHeader);
            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferKeyHeader = DotYouSystemSerializer.Deserialize<RsaEncryptedRecipientTransferInstructionSet>(json);
            return transferKeyHeader;
        }

        private async Task ProcessMetadataSection(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.Metadata);

            //TODO: determine if the filter needs to decide if its result should be sent back to the sender
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Metadata, "metadata", section.Body);
            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
        }

        private async Task ProcessPayloadSection(MultipartSection section)
        {
            if (null == section)
            {
                return;
            }

            AssertIsPart(section, MultipartHostTransferParts.Payload);

            //TODO: determine if the filter needs to decide if its result should be sent back to the sender
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Payload, "payload", section.Body);
            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
        }

        private async Task ProcessThumbnailSection(MultipartSection section)
        {
            AssertIsValidThumbnailPart(section, MultipartHostTransferParts.Thumbnail, out var fileSection, out var width, out var height);

            // section.ContentType
            string extenstion = _fileSystem.Storage.GetThumbnailFileExtension(width, height);
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Thumbnail, extenstion, section.Body);
            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
        }

        private void AssertIsValidThumbnailPart(MultipartSection section, MultipartHostTransferParts expectedPart, out FileMultipartSection fileSection, out int width, out int height)
        {
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new YouverseClientException($"Thumbnails must have name of {Enum.GetName(expectedPart)}", YouverseClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new YouverseClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')", YouverseClientErrorCode.InvalidThumnbnailName);
            }

            string[] parts = fileSection.FileName.Split('x');
            if (!Int32.TryParse(parts[0], out width) || !Int32.TryParse(parts[1], out height))
            {
                throw new YouverseClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')", YouverseClientErrorCode.InvalidThumnbnailName);
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

            throw new YouverseClientException("Invalid file system type or could not parse instruction set", YouverseClientErrorCode.InvalidFileSystemType);
        }
    }
}