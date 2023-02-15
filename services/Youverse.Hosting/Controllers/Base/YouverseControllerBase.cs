using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Base;

namespace Youverse.Hosting.Controllers.Base;

/// <summary>
/// Base utility controller for API endpoints
/// </summary>
public abstract class YouverseControllerBase : ControllerBase
{
    /// <summary />
    protected FileSystemHeaderResolver GetFileSystemResolver()
    {
        return this.HttpContext.RequestServices.GetRequiredService<FileSystemHeaderResolver>();
    }

    /// <summary>
    /// Returns the current DotYouContext from the request
    /// </summary>
    protected DotYouContext DotYouContext => HttpContext.RequestServices.GetRequiredService<DotYouContextAccessor>().GetCurrent();
}