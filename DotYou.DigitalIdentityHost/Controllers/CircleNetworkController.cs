using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers
{

    [ApiController]
    [Route("api/circlenetwork/connections")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
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
            var result = await _circleNetwork.Unblock((DotYouIdentity)dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("block/{dotYouId}")]
        public async Task<IActionResult> Block(string dotYouId)
        {
            var result = await _circleNetwork.Block((DotYouIdentity)dotYouId);
            return new JsonResult(result);
        }
        
        [HttpGet("disconnect/{dotYouId}")]
        public async Task<IActionResult> Disconnect(string dotYouId)
        {
            var result = await _circleNetwork.Disconnect((DotYouIdentity)dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("status/{dotYouId}")]
        public async Task<IActionResult> Status(string dotYouId)
        {
            var result = await _circleNetwork.GetConnectionInfo((DotYouIdentity)dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("connected")]
        public async Task<PagedResult<DotYouProfile>> GetConnectedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetConnectedProfiles(new PageOptions(pageNumber, pageSize));
            return result;
            
            
        }
        
        [HttpGet("blocked")]
        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetBlockedProfiles(new PageOptions(pageNumber, pageSize));
            return result;
        }
    }
}
