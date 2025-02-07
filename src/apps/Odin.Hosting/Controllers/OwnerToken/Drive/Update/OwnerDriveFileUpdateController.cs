using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.Update;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Drive.Update
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveFileUpdateController : DriveFileUpdateControllerBase
    {
    }
}