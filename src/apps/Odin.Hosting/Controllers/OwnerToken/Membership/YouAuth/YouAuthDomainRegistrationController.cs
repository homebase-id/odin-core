using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Util;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth
{
    [ApiController]
    [Route(OwnerApiPathConstants.YouAuthDomainManagementV1)]
    [AuthorizeValidOwnerToken]
    public class YouAuthDomainRegistrationController : OdinControllerBase
    {
        private readonly YouAuthDomainRegistrationService _registrationService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public YouAuthDomainRegistrationController(YouAuthDomainRegistrationService registrationService, TenantSystemStorage tenantSystemStorage)
        {
            _registrationService = registrationService;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Returns a list of registered domains
        /// </summary>
        [HttpGet("list")]
        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains()
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var domains = await _registrationService.GetRegisteredDomains(WebOdinContext, db);
            return domains;
        }

        /// <summary>
        /// Returns the information for a registered app; otherwise null
        /// </summary>
        [HttpPost("domain")]
        public async Task<IActionResult> GetRegisteredDomain([FromBody] GetYouAuthDomainRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var reg = await _registrationService.GetRegistration(new AsciiDomainName(request.Domain), WebOdinContext, db);
            if (null == reg)
            {
                return NotFound();
            }

            return new JsonResult(reg);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpPost("register/domain")]
        public async Task<RedactedYouAuthDomainRegistration> RegisterDomain([FromBody] YouAuthDomainRegistrationRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var reg = await _registrationService.RegisterDomain(request, WebOdinContext, db);
            return reg;
        }

        [HttpPost("circles/add")]
        public async Task<bool> GrantCircle([FromBody] GrantYouAuthDomainCircleRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _registrationService.GrantCircle(request.CircleId, new AsciiDomainName(request.Domain), WebOdinContext, db);
            return true;
        }

        [HttpPost("circles/revoke")]
        public async Task<bool> RevokeCircle([FromBody] RevokeYouAuthDomainCircleRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _registrationService.RevokeCircleAccess(request.CircleId, new AsciiDomainName(request.Domain), WebOdinContext, db);
            return true;
        }


        [HttpPost("revoke")]
        public async Task<IActionResult> RevokeDomain([FromBody] GetYouAuthDomainRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _registrationService.RevokeDomain(new AsciiDomainName(request.Domain), WebOdinContext, db);
            return Ok();
        }

        [HttpPost("allow")]
        public async Task<IActionResult> AllowDomain([FromBody] GetYouAuthDomainRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _registrationService.RemoveDomainRevocation(new AsciiDomainName(request.Domain), WebOdinContext, db);
            return Ok();
        }

        [HttpPost("deleteDomain")]
        public async Task<IActionResult> DeleteDomain([FromBody] GetYouAuthDomainRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _registrationService.DeleteDomainRegistration(new AsciiDomainName(request.Domain), WebOdinContext, db);
            return Ok();
        }

        /// <summary>
        /// Gets a list of registered clients
        /// </summary>
        /// <returns></returns>
        [HttpGet("clients")]
        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients(string domain)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var result = await _registrationService.GetRegisteredClients(new AsciiDomainName(domain), WebOdinContext, db);
            return result;
        }

        /// <summary>
        /// Deletes the client by it's access registration Id
        /// </summary>
        [HttpPost("deleteClient")]
        public async Task DeleteClient(GetYouAuthDomainClientRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _registrationService.DeleteClient(request.AccessRegistrationId, WebOdinContext, db);
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
        public async Task<YouAuthDomainClientRegistrationResponse> RegisterClient([FromBody] YouAuthDomainClientRegistrationRequest request)
        {
            //TODO: how are we going to encrypt this?
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.Domain, out _);
            OdinValidationUtils.AssertNotNullOrEmpty(request.ClientFriendlyName, nameof(request.ClientFriendlyName));

            var db = _tenantSystemStorage.IdentityDatabase;
            var (token, _) = await _registrationService.RegisterClient(new AsciiDomainName(request.Domain), request.ClientFriendlyName, null, WebOdinContext, db);

            return new YouAuthDomainClientRegistrationResponse()
            {
                AccessRegistrationId = token.Id,
                Data = token.ToPortableBytes()
            };
        }
    }
}