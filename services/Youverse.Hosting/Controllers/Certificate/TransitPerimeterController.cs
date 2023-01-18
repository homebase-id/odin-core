using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Hosting.Authentication.Perimeter;
using Youverse.Hosting.Controllers.ClientToken.Drive;

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
        private readonly ITransitPerimeterService _perimeterService;
        private Guid _stateItemId;
        private readonly IDriveService _driveService;

        public TransitPerimeterController(ITransitPerimeterService perimeterService, IDriveService driveService)
        {
            _perimeterService = perimeterService;
            _driveService = driveService;
        }

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
                var section = await reader.ReadNextSectionAsync();

                this._stateItemId = await ProcessTransferKeyHeader(section);

                //

                await ProcessMetadataSection(await reader.ReadNextSectionAsync());

                //

                await ProcessPayloadSection(await reader.ReadNextSectionAsync());

                //

                section = await reader.ReadNextSectionAsync();
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
            catch (Exception e)
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

        private async Task<Guid> ProcessTransferKeyHeader(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.TransferKeyHeader);

            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferKeyHeader = DotYouSystemSerializer.Deserialize<RsaEncryptedRecipientTransferInstructionSet>(json);

            var transferStateItemId = await _perimeterService.InitializeIncomingTransfer(transferKeyHeader);
            return transferStateItemId;
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
            string extenstion = _driveService.GetThumbnailFileExtension(width, height);
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

        private MultipartHostTransferParts GetFilePart(string name)
        {
            if (Enum.TryParse<MultipartHostTransferParts>(name, true, out var part) == false)
            {
                throw new HostToHostTransferException("Unknown MultipartHostTransferPart");
            }

            return part;
        }

        private string GetSectionName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            return cd.Name?.Trim('"');
        }

        private string GetFileName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            var filename = cd.FileName?.Trim('"');

            if (null != filename)
            {
                return filename;
            }

            return GetSectionName(contentDisposition);
        }
    }
}