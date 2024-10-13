using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    public abstract class PeerSecurityContextControllerBase(
        PeerDriveQueryService peerDriveQueryService,
        TenantSystemStorage tenantSystemStorage
        ) : OdinControllerBase
    {
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("security/context")]
        public async Task<RedactedOdinContext> GetRemoteDotYouContext([FromBody] TransitGetSecurityContextRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var ctx = await peerDriveQueryService.GetRemoteDotYouContext((OdinId)request.OdinId,WebOdinContext, db);
            return ctx;
        }
    }
}
