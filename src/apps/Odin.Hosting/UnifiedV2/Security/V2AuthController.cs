#nullable enable
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.Auth)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2AuthController(ClientRegistrationStorage storage) : OdinControllerBase
    {
        /// <summary>
        /// Verifies the ClientAuthToken (provided as a cookie order heaader for app/guest) is Valid.
        /// </summary>
        [HttpGet("verify-token")]
        [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
        public async Task<ActionResult> VerifyToken()
        {
            await base.AddUpgradeRequiredHeaderAsync();
            return Ok(true);
        }

        [HttpGet("verify-shared-secret-encryption")]
        [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
        public ActionResult VerifySharedSecret([FromQuery] string checkValue64)
        {
            var decryptedBytes = Convert.FromBase64String(checkValue64);
            return Ok(SHA256.Create().ComputeHash(decryptedBytes).ToBase64());
        }

        /// <summary>
        /// Deletes the client by its access registration Id
        /// </summary>
        [HttpPost("logout")]
        [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
        public async Task DeleteClient()
        {
            var tokenId = WebOdinContext.Caller.OdinClientContext.AccessRegistrationId;
            await storage.DeleteAsync(tokenId);
        }
    }
}