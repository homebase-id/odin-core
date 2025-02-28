using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive.Update;
using Odin.Hosting.Controllers.ClientToken.App;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveStorageV1)]
    // [Route(GuestApiPathConstants.DriveStorageV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenDriveFileUpdateController(ILogger<ClientTokenDriveFileUpdateController> logger)
        : DriveFileUpdateControllerBase(logger)
    {
    }
}