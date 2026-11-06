#nullable enable
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Home.Cache;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.SignIn;

/// <summary>
/// The home page's cached queries under <c>/api/v2/home/data/cacheable</c>: <see cref="HomePageCacheController"/> itself,
/// with V1's guest scheme.
/// </summary>
/// <remarks>
/// <b>Do not move this to the unified scheme.</b> <see cref="HomeCachingService"/> keeps one result per tenant and query
/// name, for every caller. The guest scheme never reads the owner's cookie; the unified one reads it first, so the owner
/// opening their home page would fill that cache with what the owner can see, for every anonymous visitor to read.
/// </remarks>
[Route(UnifiedApiRouteConstants.HomeCacheable)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2HomePageCacheController(HomeCachingService cachingService, TenantContext tenantContext)
    : HomePageCacheController(cachingService, tenantContext);
