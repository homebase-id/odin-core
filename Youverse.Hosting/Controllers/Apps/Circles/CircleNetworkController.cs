using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Controllers.Apps.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]

    //v.01 owner console only can send requests due to xtoken
    // [AuthorizeOwnerConsoleOrApp]
    [AuthorizeOwnerConsole]
    public class CircleNetworkController : ControllerBase
    {
        readonly ICircleNetworkService _circleNetwork;

        public CircleNetworkController(ICircleNetworkService cn)
        {
            _circleNetwork = cn;
        }

        [HttpGet("unblock/{dotYouId}")]
        public async Task<IActionResult> Unblock(string dotYouId)
        {
            var result = await _circleNetwork.Unblock((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("block/{dotYouId}")]
        public async Task<IActionResult> Block(string dotYouId)
        {
            var result = await _circleNetwork.Block((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("disconnect/{dotYouId}")]
        public async Task<IActionResult> Disconnect(string dotYouId)
        {
            var result = await _circleNetwork.Disconnect((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }


        [HttpGet("status/{dotYouId}")]
        public async Task<IActionResult> GetConnectionInfo(string dotYouId)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("connected")]
        public async Task<IActionResult> GetConnectedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetConnectedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }

        [HttpGet("blocked")]
        public async Task<IActionResult> GetBlockedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetBlockedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }
    }
}