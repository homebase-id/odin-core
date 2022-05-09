using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementDrivesV1)]
    [AuthorizeOwnerConsole]
    public class AppRegistrationDriveController : Controller
    {
        private readonly IAppRegistrationService _appRegistrationService;

        public AppRegistrationDriveController(IAppRegistrationService appRegistrationService)
        {
            _appRegistrationService = appRegistrationService;
        }
        

    }
}