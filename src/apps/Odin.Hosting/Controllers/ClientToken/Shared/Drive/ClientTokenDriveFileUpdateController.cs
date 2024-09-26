using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.Update;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveStorageV1)]
    // [Route(GuestApiPathConstants.DriveStorageV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenDriveFileUpdateController(TenantSystemStorage tenantSystemStorage) : DriveFileUpdateControllerBase(tenantSystemStorage)
    {
    }
}