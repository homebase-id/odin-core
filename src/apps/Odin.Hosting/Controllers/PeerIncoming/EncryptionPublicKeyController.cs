﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.PeerIncoming
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.EncryptionV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class EncryptionPublicKeyController(
        PublicPrivateKeyService publicPrivateKeyService,
        TenantSystemStorage tenantSystemStorage
        ) : ControllerBase
    {
        // private Guid _stateItemId;

        [HttpGet("publickey")]
        public async Task<GetPublicKeyResponse> GetRsaKey(RsaKeyType keyType)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var key = await publicPrivateKeyService.GetPublicRsaKey(keyType, cn);
            return new GetPublicKeyResponse()
            {
                PublicKey = key.publicKey,
                Crc32 = key.crc32c,
                Expiration = key.expiration.milliseconds
            };
        }
    }
}