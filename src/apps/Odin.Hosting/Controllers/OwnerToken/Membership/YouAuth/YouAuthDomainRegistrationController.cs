using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Util;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth
{
    [ApiController]
    [Route(OwnerApiPathConstants.YouAuthDomainManagementV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class YouAuthDomainRegistrationController(YouAuthDomainRegistrationService registrationService) : OdinControllerBase
    {
        /// <summary>
        /// Returns a list of registered domains
        /// </summary>
        [HttpGet("list")]
        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains()
        {
            var domains = await registrationService.GetRegisteredDomainsAsync(WebOdinContext);
            return domains;
        }

        /// <summary>
        /// Returns the information for a registered app; otherwise null
        /// </summary>
        [HttpPost("domain")]
        public async Task<IActionResult> GetRegisteredDomain([FromBody] GetYouAuthDomainRequest request)
        {
            var reg = await registrationService.GetRegistration(new AsciiDomainName(request.Domain), WebOdinContext);
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
            var reg = await registrationService.RegisterDomainAsync(request, WebOdinContext);
            return reg;
        }

        [HttpPost("circles/add")]
        public async Task<bool> GrantCircle([FromBody] GrantYouAuthDomainCircleRequest request)
        {
            await registrationService.GrantCircleAsync(request.CircleId, new AsciiDomainName(request.Domain), WebOdinContext);
            return true;
        }

        [HttpPost("circles/revoke")]
        public async Task<bool> RevokeCircle([FromBody] RevokeYouAuthDomainCircleRequest request)
        {
            await registrationService.RevokeCircleAccess(request.CircleId, new AsciiDomainName(request.Domain), WebOdinContext);
            return true;
        }

        [HttpPost("revoke")]
        public async Task<IActionResult> RevokeDomain([FromBody] GetYouAuthDomainRequest request)
        {
            await registrationService.RevokeDomainAsync(new AsciiDomainName(request.Domain), WebOdinContext);
            return Ok();
        }

        [HttpPost("allow")]
        public async Task<IActionResult> AllowDomain([FromBody] GetYouAuthDomainRequest request)
        {
            await registrationService.RemoveDomainRevocationAsync(new AsciiDomainName(request.Domain), WebOdinContext);
            return Ok();
        }

        [HttpPost("deleteDomain")]
        public async Task<IActionResult> DeleteDomain([FromBody] GetYouAuthDomainRequest request)
        {
            await registrationService.DeleteDomainRegistrationAsync(new AsciiDomainName(request.Domain), WebOdinContext);
            return Ok();
        }

        /// <summary>
        /// Gets a list of registered clients
        /// </summary>
        /// <returns></returns>
        [HttpGet("clients")]
        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients(string domain)
        {
            var result = await registrationService.GetRegisteredClientsAsync(new AsciiDomainName(domain), WebOdinContext);
            return result;
        }

        /// <summary>
        /// Deletes the client by it's access registration Id
        /// </summary>
        [HttpPost("deleteClient")]
        public async Task DeleteClient(GetYouAuthDomainClientRequest request)
        {
            await registrationService.DeleteClientAsync(request.AccessRegistrationId, WebOdinContext);
        }

        /// <summary>
        /// Registers a new client for using a specific app (a browser, app running on a phone, etc)
        /// </summary>
        [HttpPost("register/client")]
        public async Task<YouAuthDomainClientRegistrationResponse> RegisterClient([FromBody] YouAuthDomainClientRegistrationRequest request)
        {
            //TODO: how are we going to encrypt this?
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.Domain, out _);
            OdinValidationUtils.AssertNotNullOrEmpty(request.ClientFriendlyName, nameof(request.ClientFriendlyName));

            var (token, _) = await registrationService.RegisterClientAsync(new AsciiDomainName(request.Domain), request.ClientFriendlyName, null, WebOdinContext);

            return new YouAuthDomainClientRegistrationResponse()
            {
                AccessRegistrationId = token.Id,
                Data = token.ToPortableBytes()
            };
        }
    }
}