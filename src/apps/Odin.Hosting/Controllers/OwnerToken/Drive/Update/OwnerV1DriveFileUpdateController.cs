using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive.Update;
using Odin.Services.Authentication.Owner;

namespace Odin.Hosting.Controllers.OwnerToken.Drive.Update
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerV1DriveFileUpdateController(ILogger<OwnerV1DriveFileUpdateController> logger) :
        V1DriveFileUpdateControllerBase(logger)
    {
    }
}