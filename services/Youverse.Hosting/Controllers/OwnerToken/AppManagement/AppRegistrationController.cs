﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;
using System.Web;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementV1)]
    [AuthorizeValidOwnerToken]
    public class AppRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistrationService;

        public AppRegistrationController(IAppRegistrationService appRegistrationService)
        {
            _appRegistrationService = appRegistrationService;
        }


        /// <summary>
        /// Returns a list of registered apps
        /// </summary>
        [HttpGet("list")]
        public async Task<List<RedactedAppRegistration>> GetRegisteredApps()
        {
            var apps = await _appRegistrationService.GetRegisteredApps();
            return apps;
        }

        /// <summary>
        /// Returns the information for a registered app; otherwise null
        /// </summary>
        [HttpPost("app")]
        public async Task<RedactedAppRegistration> GetRegisteredApp([FromBody] GetAppRequest request)
        {
            var reg = await _appRegistrationService.GetAppRegistration(request.AppId);
            return reg;
        }

        /// <summary>
        /// Registers a new app for usage in the system
        /// </summary>
        /// <param name="appRegistration"></param>
        /// <returns></returns>
        [HttpPost("register/app")]
        public async Task<RedactedAppRegistration> RegisterApp([FromBody] AppRegistrationRequest appRegistration)
        {
            var reg = await _appRegistrationService.RegisterApp(appRegistration);
            return reg;
        }

        /// <summary>
        /// Revokes an app; this include all clients using the app and future client registrations until the revocation is removed
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("revoke")]
        public async Task<NoResultResponse> RevokeApp([FromBody] GetAppRequest request)
        {
            await _appRegistrationService.RevokeApp(request.AppId);
            return new NoResultResponse(true);
        }

        /// <summary>
        /// Removes the revocation for a given app.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("allow")]
        public async Task<NoResultResponse> AllowApp([FromBody] GetAppRequest request)
        {
            await _appRegistrationService.RemoveAppRevocation(request.AppId);
            return new NoResultResponse(true);
        }

        /// <summary>
        /// Registers a new client for using a specific app (a browser, app running on a phone, etc)
        /// </summary>
        /// <remarks>
        /// This method registers a new client (or device) for use with a specific app.
        /// <br/>
        /// <br/>
        /// It will fail if the app is not registered or is revoked
        /// <br/>
        /// The friendly name is good for identifying the client in the owner console at a later time.  (i.e. I want to see all devices/clients using
        /// an app.  Set it to something like the computer name or phone name (i.e. Todd's android).
        /// 
        /// The ClientPublicKey64 is a base64 encoded byte array of an RSA public key generated by the client.  This will be used to encrypt the response
        /// as it contains sensitive data.
        /// </remarks>
        [HttpPost("register/client")]
        public async Task<AppClientRegistrationResponse> RegisterClient([FromBody] AppClientRegistrationRequest request)
        {
            // var b64 = HttpUtility.UrlDecode(request.ClientPublicKey64);
            // var clientPublicKey = Convert.FromBase64String(b64);
            var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey64);
            var reg = await _appRegistrationService.RegisterClient(request.AppId, clientPublicKey, request.ClientFriendlyName);
            return reg;
        }
    }
}