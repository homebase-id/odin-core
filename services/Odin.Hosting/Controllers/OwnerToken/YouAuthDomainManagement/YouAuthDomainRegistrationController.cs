using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.YouAuth;
using Odin.Core.Util;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuthDomainManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.YouAuthDomainManagementV1)]
    [AuthorizeValidOwnerToken]
    public class YouAuthDomainRegistrationController : Controller
    {
        private readonly YouAuthDomainRegistrationService _registrationService;

        public YouAuthDomainRegistrationController(YouAuthDomainRegistrationService registrationService)
        {
            _registrationService = registrationService;
        }


        /// <summary>
        /// Returns a list of registered domains
        /// </summary>
        [HttpGet("list")]
        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains()
        {
            var apps = await _registrationService.GetRegisteredDomains();
            return apps;
        }

        /// <summary>
        /// Returns the information for a registered app; otherwise null
        /// </summary>
        [HttpPost("domain")]
        public async Task<RedactedYouAuthDomainRegistration> GetRegisteredDomain([FromBody] GetYouAuthDomainRequest request)
        {
            var reg = await _registrationService.GetRegistration(new AsciiDomainName(request.Domain));
            return reg;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpPost("register/domain")]
        public async Task<RedactedYouAuthDomainRegistration> RegisterDomain([FromBody] YouAuthDomainRegistrationRequest request)
        {
            var reg = await _registrationService.RegisterDomain(request);
            return reg;
        }

        /// <summary>
        /// Updates the app's permissions
        /// </summary>
        /// <returns></returns>
        [HttpPost("register/updatepermissions")]
        public async Task UpdatePermissions([FromBody] UpdateYouAuthDomainPermissionsRequest request)
        {
            await _registrationService.UpdatePermissions(request);
        }
        
        [HttpPost("revoke")]
        public async Task<NoResultResponse> RevokeApp([FromBody] GetYouAuthDomainRequest request)
        {
            await _registrationService.RevokeDomain(new AsciiDomainName(request.Domain));
            return new NoResultResponse(true);
        }

        [HttpPost("allow")]
        public async Task<NoResultResponse> AllowApp([FromBody] GetYouAuthDomainRequest request)
        {
            await _registrationService.RemoveDomainRevocation(new AsciiDomainName(request.Domain));
            return new NoResultResponse(true);
        }

       
        [HttpPost("deleteApp")]
        public async Task<NoResultResponse> DeleteApp([FromBody] GetYouAuthDomainRequest request)
        {
            await _registrationService.DeleteDomainRegistration(new AsciiDomainName(request.Domain));
            return new NoResultResponse(true);
        }


        /// <summary>
        /// Gets a list of registered clients
        /// </summary>
        /// <returns></returns>
        [HttpGet("clients")]
        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients()
        {
            var result = await _registrationService.GetRegisteredClients();
            return result;
        }

        /// <summary>
        /// Revokes the client by it's access registration Id
        /// </summary>
        [HttpPost("revokeClient")]
        public async Task RevokeClient(GetYouAuthDomainClientRequest request)
        {
            await _registrationService.RevokeClient(request.AccessRegistrationId);
        }

        /// <summary>
        /// Re-enables the client by it's access registration Id
        /// </summary>
        [HttpPost("allowClient")]
        public async Task EnableClient(GetYouAuthDomainClientRequest request)
        {
            await _registrationService.AllowClient(request.AccessRegistrationId);
        }

        /// <summary>
        /// Deletes the client by it's access registration Id
        /// </summary>
        [HttpPost("deleteClient")]
        public async Task DeleteClient(GetYouAuthDomainClientRequest request)
        {
            await _registrationService.DeleteClient(request.AccessRegistrationId);
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
        public AppClientRegistrationResponse RegisterClient([FromBody] YouAuthDomainClientRegistrationRequest request)
        {
            // var b64 = HttpUtility.UrlDecode(request.ClientPublicKey64);
            // // var clientPublicKey = Convert.FromBase64String(b64);
            // var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey64);
            // var (reg, corsHostName) = await _registrationService.RegisterClient(new AsciiDomainName(request.Domain), request.ClientPublicKey, request.ClientFriendlyName);
            // return reg;
            return null;
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
        [HttpPost("register/hostedapp/client")]
        public IActionResult RegisterClientWithRedirect([FromBody] YouAuthDomainClientRegistrationRequest request)
        {
            // var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey64);
            // var (reg, corsHostName) = await _registrationService.RegisterClient(request., clientPublicKey, request.ClientFriendlyName);
            //
            // if (string.IsNullOrEmpty(corsHostName))
            // {
            //     return BadRequest("App does not have host name");
            // }
            //
            // var qs = OdinSystemSerializer.Serialize(reg);
            //
            // var uri = new UriBuilder
            // {
            //     Scheme = "https",
            //     Host = corsHostName,
            //     Query = $"d={qs}"
            // };
            //
            // HttpContext.Response.Redirect(uri.ToString());
            return Ok();
            // return Redirect(uri.ToString());
        }
    }
}