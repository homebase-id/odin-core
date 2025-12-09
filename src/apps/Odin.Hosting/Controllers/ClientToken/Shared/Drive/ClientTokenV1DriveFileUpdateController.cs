using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive.Update;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstantsV1.DriveStorageV1)]
    [Route(GuestApiPathConstantsV1.DriveStorageV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenV1DriveFileUpdateController(ILogger<ClientTokenV1DriveFileUpdateController> logger)
        : V1DriveFileUpdateControllerBase(logger)
    {
    }
}