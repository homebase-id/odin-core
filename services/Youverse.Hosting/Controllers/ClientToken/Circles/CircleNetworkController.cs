using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [Route(YouAuthApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidExchangeGrant]
    public class CircleNetworkController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;

        public CircleNetworkController(ICircleNetworkService cn)
        {
            _circleNetwork = cn;
        }

        /// <summary>
        /// Gets a list of connected identities
        /// </summary>
        /// <returns></returns>
        [HttpGet("connected")]
        public async Task<CursoredResult<long, RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor,
            bool omitContactData = false)
        {
            var result = await _circleNetwork.GetConnectedIdentities(count, cursor);
            return new CursoredResult<long, RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }
    }
}