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
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="omitContactData">If true, the OriginalContactData field is not returned; default is true</param>
        /// <returns></returns>
        [HttpGet("connected")]
        public async Task<PagedResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int pageNumber, int pageSize, bool omitContactData = true)
        {
            var result = await _circleNetwork.GetConnectedIdentities(new PageOptions(pageNumber, pageSize));
            return new PagedResult<RedactedIdentityConnectionRegistration>(
                result.Request,
                result.TotalPages,
                result.Results.Select(p => p.Redacted(omitContactData)).ToList());
        }
    }
}