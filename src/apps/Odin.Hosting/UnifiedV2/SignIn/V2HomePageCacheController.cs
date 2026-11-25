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
/// <see cref="HomeCachingService"/> keeps one result per tenant and query for every caller, and builds it as an anonymous
/// visitor whoever asked, so the scheme decides only who may invalidate it, never what it holds.
/// </remarks>
[Route(UnifiedApiRouteConstants.HomeCacheable)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2HomePageCacheController(HomeCachingService cachingService, TenantContext tenantContext)
    : HomePageCacheController(cachingService, tenantContext);
