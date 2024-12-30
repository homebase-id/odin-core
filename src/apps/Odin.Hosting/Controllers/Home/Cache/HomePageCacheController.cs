using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Home.Cache;

[ApiController]
[Route(HomeApiPathConstants.CacheableV1)]
[AuthorizeValidGuestOrAppToken]
public class HomePageCacheController : OdinControllerBase
{
    private readonly HomeCachingService _cachingService;
    private readonly TenantContext _tenantContext;


    public HomePageCacheController(HomeCachingService cachingService, TenantContext tenantContext)
    {
        _cachingService = cachingService;
        _tenantContext = tenantContext;
        
    }

    private const string HomePageSwaggerTag = "Home Page Data";

    [SwaggerOperation(Tags = new[] { HomePageSwaggerTag })]
    [HttpPost("invalidate")]
    public IActionResult InvalidCache()
    {
        _cachingService.Invalidate();
        return Ok();
    }

    [SwaggerOperation(Tags = new[] { HomePageSwaggerTag })]
    [HttpPost("qbc")]
    public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
    {
        
        return await this.GetOrCache(request);
    }

    [SwaggerOperation(Tags = new[] { HomePageSwaggerTag })]
    [HttpGet("qbc")]
    public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromQuery] GetCollectionQueryParamSection[] queries)
    {
        var sections = new List<CollectionQueryParamSection>();
        foreach (var query in queries)
        {
            var section = query.ToCollectionQueryParamSection();
            section.AssertIsValid();
            sections.Add(section);
        }

        var request = new QueryBatchCollectionRequest()
        {
            Queries = sections
        };

        var result = await GetOrCache(request);
        return result;
    }

    private async Task<QueryBatchCollectionResponse> GetOrCache(QueryBatchCollectionRequest request)
    {
        // tell the browser to check in ever 1 minutes
        const int minutes = 1;
        AddGuestApiCacheHeader(minutes);
        return await _cachingService.GetResult(request, WebOdinContext, _tenantContext.HostOdinId);
    }
}