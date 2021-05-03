using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotYou.TenantHost.Controllers
{
    [Route("api/trustnetwork")]
    [ApiController]
    public class TrustNetworkController: ControllerBase
    {
        ITrustNetworkService _trustNetwork;
        public TrustNetworkController(ITrustNetworkService trustNetwork)
        {
            _trustNetwork = trustNetwork;
        }

        [HttpGet]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<PagedResult<ConnectionRequest>> GetList(int pageNumber, int pageSize)
        {
            var result = await _trustNetwork.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }


        [HttpGet("{id}")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<ConnectionRequest> Get(Guid id)
        {
            return await _trustNetwork.GetPendingRequest(id);
        }

        [HttpPost()]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public void Create([FromBody] ConnectionRequest request)
        {
            _trustNetwork.SendConnectionRequest(request);
        }

        [HttpPost("accept")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public void Accept(Guid invitationId)
        {
        }

        [HttpDelete]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<IActionResult> Delete(Guid invitationId)
        {
            await _trustNetwork.DeletePendingRequest(invitationId);
            return Ok();
        }

    }
}
