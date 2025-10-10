#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Optimization.Cdn;
using Odin.Services.Tenant;
using Odin.Services.Util;
using Odin.Core.Time;
using Odin.Services.LinkPreview;

namespace Odin.Hosting.Controllers.Anonymous
{
    [ApiController]
    public class StaticFileController(
        ITenantProvider tenantProvider,
        StaticFileContentService staticFileContentService)
        : Controller
    {
        private readonly string _currentTenant = tenantProvider.GetCurrentTenant()!.Name;

        //

        /// <summary>
        /// Returns the static file's contents
        /// </summary>
        /// <returns></returns>
        [HttpGet("cdn/{filename}")]
        public async Task<IActionResult> GetStaticFile(string filename)
        {
            return await this.SendStream(filename);
        }

        /// <summary>
        /// Returns the public profile image
        /// </summary>
        [HttpGet("pub/image")]
        [HttpGet(LinkPreviewDefaults.PublicImagePath)]
        public async Task<IActionResult> GetPublicImage()
        {
            return await this.SendStream(StaticFileConstants.ProfileImageFileName, StaticFileConstants.FallBackProfileImage, "image/jpeg");
        }

        /// <summary>
        /// Returns the public profile data
        /// </summary>
        [HttpGet("pub/profile")]
        public async Task<IActionResult> GetPublicProfileData()
        {
            return await this.SendStream(StaticFileConstants.PublicProfileCardFileName);
        }


        private async Task<IActionResult> SendStream(string filename, string? fallbackContent64 = null, string? fallbackContentType = null)
        {
            OdinValidationUtils.AssertValidFileName(filename, "The filename is invalid");
            var (config, fileExists, bytes) = await staticFileContentService.GetStaticFileStreamAsync(filename, GetIfModifiedSince());

            if (fileExists && bytes == null)
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            string contentType = config.ContentType;
            //sanity
            if (!fileExists || bytes == null || bytes.Length == 0)
            {
                if (!string.IsNullOrEmpty(fallbackContent64) && !string.IsNullOrEmpty(fallbackContentType))
                {
                    contentType = fallbackContentType;
                    bytes =  fallbackContent64.FromBase64();
                }
                else
                {
                    return NotFound();
                }
            }

            if (config.CrossOriginBehavior == CrossOriginBehavior.AllowAllOrigins)
            {
                if (Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
                {
                    Response.Headers.Remove("Access-Control-Allow-Origin");
                }

                Response.Headers.Append("Access-Control-Allow-Origin", "*");
            }

            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(config.LastModified);
            this.Response.Headers.TryAdd("Cache-Control", "max-age=3600, stale-while-revalidate=31536000");

            return new FileContentResult(bytes, contentType);
        }

        //

        private UnixTimeUtc? GetIfModifiedSince()
        {
            if (Request.Headers.TryGetValue(HttpHeaderConstants.IfModifiedSince, out var values))
            {
                DateTime modifiedSinceDatetime = DateTime.Now;
                var headerValue = values.FirstOrDefault();
                var hasValidDate = headerValue != null && DateTime.TryParse(headerValue, out modifiedSinceDatetime);
                if (hasValidDate)
                {
                    return UnixTimeUtc.FromDateTime(modifiedSinceDatetime);
                }
            }

            return null;
        }
    }
}