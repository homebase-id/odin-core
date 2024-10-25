using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementV1)]
    [AuthorizeValidOwnerToken]
    public class AppRegistrationController : OdinControllerBase
    {
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public AppRegistrationController(IAppRegistrationService appRegistrationService, TenantSystemStorage tenantSystemStorage)
        {
            _appRegistrationService = appRegistrationService;
            _tenantSystemStorage = tenantSystemStorage;
        }


        /// <summary>
        /// Returns a list of registered apps
        /// </summary>
        [HttpGet("list")]
        public async Task<List<RedactedAppRegistration>> GetRegisteredApps()
        {
            var apps = await _appRegistrationService.GetRegisteredAppsAsync(WebOdinContext);
            return apps;
        }

        /// <summary>
        /// Returns the information for a registered app; otherwise null
        /// </summary>
        [HttpPost("app")]
        public async Task<RedactedAppRegistration> GetRegisteredApp([FromBody] GetAppRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var reg = await _appRegistrationService.GetAppRegistration(request.AppId, WebOdinContext);
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
            OdinValidationUtils.AssertNotNull(appRegistration, nameof(appRegistration));
            OdinValidationUtils.AssertIsTrue(appRegistration.IsValid(), "The app registration is invalid");

            var reg = await _appRegistrationService.RegisterAppAsync(appRegistration, WebOdinContext);
            return reg;
        }

        /// <summary>
        /// Updates the app's permissions
        /// </summary>
        /// <returns></returns>
        [HttpPost("register/updateapppermissions")]
        public async Task UpdateAppPermissions([FromBody] UpdateAppPermissionsRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.UpdateAppPermissionsAsync(request, WebOdinContext);
        }

        /// <summary>
        /// Updates the authorized circles and their permissions
        /// </summary>
        [HttpPost("register/updateauthorizedcircles")]
        public async Task UpdateAuthorizedCircles([FromBody] UpdateAuthorizedCirclesRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.UpdateAuthorizedCirclesAsync(request, WebOdinContext);
        }

        /// <summary>
        /// Revokes an app; this include all clients using the app and future client registrations until the revocation is removed
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("revoke")]
        public async Task<NoResultResponse> RevokeApp([FromBody] GetAppRequest request)
        {
            await _appRegistrationService.RevokeAppAsync(request.AppId, WebOdinContext);
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
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.RemoveAppRevocationAsync(request.AppId, WebOdinContext);
            return new NoResultResponse(true);
        }

        /// <summary>
        /// Removes the revocation for a given app.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("deleteApp")]
        public async Task<NoResultResponse> DeleteApp([FromBody] GetAppRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.DeleteAppAsync(request.AppId, WebOdinContext);
            return new NoResultResponse(true);
        }


        /// <summary>
        /// Gets a list of registered clients
        /// </summary>
        /// <returns></returns>
        [HttpPost("clients")]
        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients([FromBody] GetAppRequest request)
        {
            var result = await _appRegistrationService.GetRegisteredClientsAsync(request.AppId, WebOdinContext);
            return result;
        }

        /// <summary>
        /// Revokes the client by it's access registration Id
        /// </summary>
        [HttpPost("revokeClient")]
        public async Task RevokeClient(GetAppClientRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.RevokeClientAsync(request.AccessRegistrationId, WebOdinContext);
        }

        /// <summary>
        /// Re-enables the client by it's access registration Id
        /// </summary>
        [HttpPost("allowClient")]
        public async Task EnableClient(GetAppClientRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.AllowClientAsync(request.AccessRegistrationId, WebOdinContext);
        }

        /// <summary>
        /// Deletes the client by it's access registration Id
        /// </summary>
        [HttpPost("deleteClient")]
        public async Task DeleteClient(GetAppClientRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _appRegistrationService.DeleteClientAsync(request.AccessRegistrationId, WebOdinContext);
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
            var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey64);

            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsTrue(request.AppId != Guid.Empty, "missing app id");
            OdinValidationUtils.AssertNotNullOrEmpty(request.ClientFriendlyName, nameof(request.ClientFriendlyName));

            var db = _tenantSystemStorage.IdentityDatabase;
            var (reg, _) = await _appRegistrationService.RegisterClientPkAsync(request.AppId, clientPublicKey, request.ClientFriendlyName, WebOdinContext);
            return reg;
        }
    }
}