using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.Base;

/// <summary>
/// Base utility controller for API endpoints
/// </summary>
public abstract class OdinControllerBase : ControllerBase
{
    /// <summary />
    protected FileSystemHttpRequestResolver GetFileSystemResolver()
    {
        return this.HttpContext.RequestServices.GetRequiredService<FileSystemHttpRequestResolver>();
    }

    /// <summary />
    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file)
    {
        return  new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = DotYouContext.PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }
    
    /// <summary>
    /// Returns the current DotYouContext from the request
    /// </summary>
    protected DotYouContext DotYouContext => HttpContext.RequestServices.GetRequiredService<DotYouContextAccessor>().GetCurrent();
}