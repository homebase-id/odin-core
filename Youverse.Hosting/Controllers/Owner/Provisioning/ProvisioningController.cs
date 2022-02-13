﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Profile;
using Youverse.Core.Services.Registry.Provisioning;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Provisioning
{
    [ApiController]
    [Route(OwnerApiPathConstants.ProvisioningV1)]
    [AuthorizeOwnerConsole]
    public class ProvisioningController : Controller
    {
        private readonly IIdentityProvisioner _identityProvisioner;

        public ProvisioningController(IIdentityProvisioner identityProvisioner)
        {
            _identityProvisioner = identityProvisioner;
        }

        //TODO: will need to send a callbackId or increase the timeout
        [HttpPost("systemapps")]
        public async Task EnsureSystemApps()
        {
            await _identityProvisioner.EnsureSystemApps();
        }
    }
}