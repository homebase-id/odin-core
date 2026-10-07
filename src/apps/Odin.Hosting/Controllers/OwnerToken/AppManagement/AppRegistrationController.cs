using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Apps;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class AppRegistrationController(
        IAppRegistrationService appRegistrationService,
        IYouAuthUnifiedService youAuthUnifiedService,
        AppUninstallService appUninstallService)
        : OdinControllerBase
    {
        /// <summary>
        /// Returns a list of registered apps
        /// </summary>
        [HttpGet("list")]
        public async Task<List<RedactedAppRegistration>> GetRegisteredApps()
        {
            var apps = await appRegistrationService.GetRegisteredAppsAsync(WebOdinContext);
            return apps;
        }

        /// <summary>
        /// Returns the information for a registered app; otherwise null
        /// </summary>
        [HttpPost("app")]
        public async Task<RedactedAppRegistration> GetRegisteredApp([FromBody] GetAppRequest request)
        {
            var reg = await appRegistrationService.GetAppRegistration(request.AppId, WebOdinContext);
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

            var reg = await appRegistrationService.RegisterAppAsync(appRegistration, WebOdinContext);
            return reg;
        }

        /// <summary>
        /// Updates the app's permissions
        /// </summary>
        /// <returns></returns>
        [HttpPost("register/updateapppermissions")]
        public async Task UpdateAppPermissions([FromBody] UpdateAppPermissionsRequest request)
        {
            await appRegistrationService.UpdateAppPermissionsAsync(request, WebOdinContext);
        }

        /// <summary>
        /// Updates the authorized circles and their permissions
        /// </summary>
        [HttpPost("register/updateauthorizedcircles")]
        public async Task UpdateAuthorizedCircles([FromBody] UpdateAuthorizedCirclesRequest request)
        {
            await appRegistrationService.UpdateAuthorizedCirclesAsync(request, WebOdinContext);
        }

        /// <summary>
        /// Revokes an app; this include all clients using the app and future client registrations until the revocation is removed
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("revoke")]
        public async Task<NoResultResponse> RevokeApp([FromBody] GetAppRequest request)
        {
            await appRegistrationService.RevokeAppAsync(request.AppId, WebOdinContext);
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
            await appRegistrationService.RemoveAppRevocationAsync(request.AppId, WebOdinContext);
            return new NoResultResponse(true);
        }

        /// <summary>
        /// Uninstalls a third-party app fully: its clients and their push subscriptions, its grants on every
        /// connection, and its registration. An app that owns circles or drives is refused unless
        /// <see cref="UninstallAppRequest.DeleteOwnedCirclesAndDrives"/> says they go with it.
        /// </summary>
        [HttpPost("uninstall")]
        public async Task<NoResultResponse> UninstallApp([FromBody] UninstallAppRequest request)
        {
            await appUninstallService.UninstallAsync(request.AppId, request.DeleteOwnedCirclesAndDrives, WebOdinContext);
            return new NoResultResponse(true);
        }


        /// <summary>
        /// Gets a list of registered clients
        /// </summary>
        /// <returns></returns>
        [HttpPost("clients")]
        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients([FromBody] GetAppRequest request)
        {
            var result = await appRegistrationService.GetRegisteredClientsAsync(request.AppId, WebOdinContext);
            return result;
        }

        /// <summary>
        /// Revokes the client by it's access registration Id
        /// </summary>
        [HttpPost("revokeClient")]
        public async Task RevokeClient(GetAppClientRequest request)
        {
            await appRegistrationService.RevokeClientAsync(request.AccessRegistrationId, WebOdinContext);
        }

        /// <summary>
        /// Re-enables the client by it's access registration Id
        /// </summary>
        [HttpPost("allowClient")]
        public async Task EnableClient(GetAppClientRequest request)
        {
            await appRegistrationService.AllowClientAsync(request.AccessRegistrationId, WebOdinContext);
        }

        /// <summary>
        /// Deletes the client by it's access registration Id
        /// </summary>
        [HttpPost("deleteClient")]
        public async Task DeleteClient(GetAppClientRequest request)
        {
            await appRegistrationService.DeleteClientAsync(request.AccessRegistrationId, WebOdinContext);
        }

        [HttpPost("register/client-ecc")]
        public async Task<AppClientEccRegistrationResponse> RegisterClientUsingEcc([FromBody] AppClientRegistrationRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsTrue(request.AppId != Guid.Empty, "missing app id");
            OdinValidationUtils.AssertNotNullOrEmpty(request.ClientFriendlyName, nameof(request.ClientFriendlyName));

            var (exchangePublicKeyJwkBase64Url, exchangeSalt) = await youAuthUnifiedService.CreateClientAccessTokenAsync(
                ClientType.app,
                request.AppId.ToString(),
                request.ClientFriendlyName,
                permissionRequest: "",
                jwkbase64UrlPublicKey: request.JwkBase64UrlPublicKey,
                YouAuthCipher.AesCbc,
                WebOdinContext);

            return new AppClientEccRegistrationResponse
            {
                EncryptionVersion = 1,
                ExchangePublicKeyJwkBase64Url = exchangePublicKeyJwkBase64Url,
                ExchangeSalt64 = exchangeSalt
            };
        }

        [HttpPost("register/client-ecc-exchange")]
        public async Task<ActionResult<YouAuthTokenResponse>> Exchange([FromBody] YouAuthTokenRequest request)
        {
            var accessToken = await youAuthUnifiedService.ExchangeDigestForEncryptedToken(request.SecretDigest);

            if (null == accessToken)
            {
                return NotFound();
            }

            return YouAuthTokenResponse.From(accessToken);
        }
    }
}