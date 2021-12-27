using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route("/api/app/v1/drive")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class DriveDataController : Controller
    {
        

    }
}