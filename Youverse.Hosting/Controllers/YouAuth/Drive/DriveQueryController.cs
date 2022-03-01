﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.YouAuth;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Controllers.YouAuth.Drive
{
    [ApiController]
    [Route(YouAuthApiPathConstants.DrivesV1 + "/query")]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    public class DriveQueryController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveQueryService _driveQueryService;

        public DriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
        }

        // [HttpGet("filetype")]
        // public async Task<IActionResult> GetByFileType(int fileType, bool includeContent, int pageNumber, int pageSize)
        // {
        //     var driveId = _context.GetCurrent().AppContext.DriveId.GetValueOrDefault();
        //     var page = await _driveQueryService.GetByFiletype(driveId, fileType, includeContent, new PageOptions(pageNumber, pageSize));
        //     return new JsonResult(page);
        // }

        [HttpGet("tag")]
        public async Task<IActionResult> GetByTag(Guid driveIdentifier, Guid tag, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier);

            var page = await _driveQueryService.GetByTag(driveId, tag, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(Guid driveIdentifier, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier);
            var page = await _driveQueryService.GetRecentlyCreatedItems(driveId, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpPost("rebuild")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _driveQueryService.RebuildBackupIndex(driveId);
            return true;
        }
    }
}