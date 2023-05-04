﻿#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;

namespace Youverse.Hosting.Controllers.Anonymous
{
    public class GetIdentResponse
    {
        public string? OdinId { get; set; }
        public double Version { get; set; }
    }
    
    [ApiController]
    [Route(YouAuthApiPathConstants.AuthV1)]
    public class IdentController : Controller
    {
        private readonly DotYouContextAccessor _dotYouContextAccessor;
        private readonly IIdentityRegistry _registry;


        public IdentController(DotYouContextAccessor dotYouContextAccessor, IIdentityRegistry registry)
        {
            _dotYouContextAccessor = dotYouContextAccessor;
            _registry = registry;
        }

        /// <summary>
        /// Identifies this server as an ODIN identity server
        /// </summary>
        [HttpGet("ident")]
        [Produces("application/json")]
        public async Task<IActionResult> GetInfo()
        {
            var tenant = _dotYouContextAccessor.GetCurrent().Tenant;
            HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            if (string.IsNullOrEmpty(tenant))
            {
                return await Task.FromResult(new JsonResult(new GetIdentResponse()
                {
                    OdinId = string.Empty,
                    Version = 1.0
                }));
            }
            
            if(await _registry.IsIdentityRegistered(tenant))
            {
                return await Task.FromResult(new JsonResult(new GetIdentResponse()
                {
                    OdinId = tenant,
                    Version = 1.0
                }));
            }
            
            return await Task.FromResult(new JsonResult(new GetIdentResponse
            {
                OdinId = string.Empty,
                Version = 1.0
            }));
        }
    }
}