﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.App;


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route("/api/apps/v1/drive")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class DriveStorageController : ControllerBase
    {
        private readonly IDriveService _driveService;
        private readonly IDriveQueryService _queryService;
        
        //Note: the _packageStorageWriter exists so we have a service that holds
        //the logic and routing of tenant-specific data.  We don't
        //want that in the http controllers
        private readonly IMultipartPackageStorageWriter _packageStorageWriter;
        private readonly DotYouContext _context;

        public DriveStorageController(IMultipartPackageStorageWriter packageStorageWriter,  DotYouContext context, IDriveService driveService, IDriveQueryService queryService)
        {
            _packageStorageWriter = packageStorageWriter;
            _context = context;
            _driveService = driveService;
            _queryService = queryService;
        }

        /// <summary>
        /// Accepts a multipart upload.  The 'name' parameter in the upload must be specified.  The following parts are required:
        ///
        /// name: "metadata": an encrypted object of metadata information in json format (fields/format is TBD as of dec 21, 2021)
        /// name: "payload": the encrypted payload of data
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        [HttpPost("store")]
        public async Task<IActionResult> Store()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new InvalidDataException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            //TODO: need to enable specifying a different drive
            
            // var drive = driveId ?? _context.AppContext.DriveId.GetValueOrDefault();
            var drive = _context.AppContext.DriveId.GetValueOrDefault();
            var packageId = await _packageStorageWriter.CreatePackage(drive, 3);
            
            bool isComplete = false;
            var section = await reader.ReadNextSectionAsync();
            while (section != null || !isComplete)
            {
                var partName = GetSectionName(section.ContentDisposition);
                var partStream = section.Body;
                isComplete = await _packageStorageWriter.AddPart(packageId, partName, partStream);
                section = await reader.ReadNextSectionAsync();
            }

            if (!isComplete)
            {
                throw new InvalidDataException("Upload does not contain all required parts.");
            }

            var package = await _packageStorageWriter.GetPackage(packageId);
            
            await _driveService.MoveToLongTerm(package.File);

            return new JsonResult(package.File);
        }

        private static bool IsMultipartContentType(string contentType)
        {
            return
                !string.IsNullOrEmpty(contentType) &&
                contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
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
    }

    // public class StorageResult
    // {
    //     Guid FileId
    // }
}