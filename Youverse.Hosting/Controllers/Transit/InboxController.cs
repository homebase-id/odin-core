using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Inbox;
using Youverse.Hosting.Security;

namespace Youverse.Hosting.Controllers.Transit
{
    [ApiController]
    [Route("/api/transit/client/inbox")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class InboxController : ControllerBase
    {
        private readonly IInboxService _inbox;

        public InboxController(ITransitService svc, IInboxService inbox)
        {
            _inbox = inbox;
        }
        
        [HttpGet]
        public async Task<IActionResult> GetList(int pageNumber, int pageSize)
        {
            try
            {
                var items = await _inbox.GetPendingItems(new PageOptions(pageNumber, pageSize));
                return new JsonResult(items);
            }
            catch (InvalidDataException e)
            {
                return new JsonResult(new NoResultResponse(false, e.Message));
            }
        }

        [HttpGet("item")]
        public async Task<IActionResult> GetItem(Guid id)
        {
            var items = await _inbox.GetItem(id);
            return new JsonResult(items);
        }

        [HttpDelete("item")]
        public async Task<IActionResult> RemoveItem(Guid id)
        {
            await _inbox.RemoveItem(id);
            return new JsonResult(true);
        }
    }
}