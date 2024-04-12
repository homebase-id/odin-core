using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.Home.Service;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Home.Cache;

[ApiController]
[Route(HomeApiPathConstants.CacheableV1)]
[AuthorizeValidGuestOrAppToken]
public class HomePageCacheController : OdinControllerBase
{
    private readonly HomeCachingService _cachingService;

    public HomePageCacheController(HomeCachingService cachingService)
    {
        _cachingService = cachingService;
    }

    private const string HomePageSwaggerTag = "Home Page Data";

    [SwaggerOperation(Tags = new[] { HomePageSwaggerTag })]
    [HttpPost("invalidate")]
    public async Task<IActionResult> InvalidCache()
    {
        _cachingService.Invalidate();
        return await Task.FromResult(Ok());
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

        var result = await this.GetOrCache(request);
        return result;
    }

    private async Task<QueryBatchCollectionResponse> GetOrCache(QueryBatchCollectionRequest request)
    {
        // tell the browser to check in ever 1 minutes
        const int minutes = 1;
        AddGuestApiCacheHeader(minutes);
        return await _cachingService.GetResult(request, this.GetFileSystemType());
    }
}
