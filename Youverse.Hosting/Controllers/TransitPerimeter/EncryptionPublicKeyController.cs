using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Hosting.Authentication.TransitPerimeter;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/encryption")]
    [Authorize(Policy = TransitPerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = TransitPerimeterAuthConstants.TransitAuthScheme)]
    public class EncryptionPublicKeyController : ControllerBase
    {
        private readonly IPublicKeyService _publicKeyService;
        private Guid _stateItemId;

        public EncryptionPublicKeyController(IPublicKeyService publicKeyService)
        {
            _publicKeyService = publicKeyService;
        }

        [HttpGet("offlinekey")]
        public async Task<JsonResult> GetOfflinePublicKey()
        {
            var key = await _publicKeyService.GetOfflinePublicKey();
            return new JsonResult(key);
        }
    }
}