using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.Incoming;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.TransitV1 + "/inbox/processor")]
    [AuthorizeValidOwnerToken]
    public class OwnerTransitProcessController : TransitProcessControllerBase
    {
        public OwnerTransitProcessController(TransitInboxProcessor transitInboxProcessor):base(transitInboxProcessor)
        {
        }
    }
}