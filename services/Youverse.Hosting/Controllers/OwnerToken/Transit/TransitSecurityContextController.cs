using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.ClientToken.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitQueryV1)]
    [AuthorizeValidOwnerToken]
    public class TransitSecurityContextController : OdinControllerBase
    {
        private readonly TransitQueryService _transitQueryService;

        public TransitSecurityContextController(TransitQueryService transitQueryService)
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
