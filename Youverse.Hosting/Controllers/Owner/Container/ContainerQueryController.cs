using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Container.Query;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Container
{
    [ApiController]
    [Route("/api/owner/v1/container")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class ContainerQueryController : ControllerBase
    {
        private readonly IContainerQueryService _queryService;

        public ContainerQueryController(IContainerQueryService queryService)
        {
            _queryService = queryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetList(Guid containerId, Guid categoryId, bool includeContent, int pageNumber, int pageSize)
        {
            var page = await _queryService.GetItemsByCategory(containerId, categoryId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
        
        [HttpGet]
        public async Task<IActionResult> GetRecent(Guid containerId,  bool includeContent, int pageNumber, int pageSize)
        {
            var page = await _queryService.GetRecentlyCreatedItems(containerId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
    }
}