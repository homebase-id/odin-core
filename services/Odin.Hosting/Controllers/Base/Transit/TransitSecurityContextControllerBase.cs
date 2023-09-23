using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.OwnerToken;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    public class TransitSecurityContextControllerBase : OdinControllerBase
    {
        private readonly TransitQueryService _transitQueryService;

        public TransitSecurityContextControllerBase(TransitQueryService transitQueryService)
        {
            _transitQueryService = transitQueryService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("security/context")]
        public async Task<RedactedOdinContext> GetRemoteDotYouContext([FromBody] TransitGetSecurityContextRequest request)
        {
            var ctx = await _transitQueryService.GetRemoteDotYouContext((OdinId)request.OdinId);
            return ctx;
        }
    }
}
