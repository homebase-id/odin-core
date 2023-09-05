using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Transit;
using Odin.Core.Services.Transit.ReceivingHost;
using Odin.Core.Services.Transit.ReceivingHost.Quarantine;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Peer
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.SecurityV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterSecurityController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;

        public PeerPerimeterSecurityController(OdinContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        /// <summary />
        [HttpGet("context")]
        public Task<RedactedOdinContext> GetRemoteSecurityContext()
        {
            return Task.FromResult(_contextAccessor.GetCurrent().Redacted());
        }
    }
}