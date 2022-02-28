﻿using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Registry.Provisioning
{
    public class IdentityProvisioner : IIdentityProvisioner
    {
        private readonly IAppRegistrationService _appRegService;

        public IdentityProvisioner(IAppRegistrationService appRegService)
        {
            _appRegService = appRegService;
        }

        public async Task EnsureSystemApps()
        {
            await SetupProfile();
            await SetupWebHomeApp();
            await SetupChat();
        }

        private async Task SetupProfile()
        {
            string profileAppName = "Profile Data";

            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.ProfileAppId);
            if (null == existingApp)
            {
                var appReg = await _appRegService.RegisterApp(
                    SystemAppConstants.ProfileAppId,
                    profileAppName,
                    SystemAppConstants.ProfileAppStandardProfileDriveIdentifier,
                    createDrive: true,
                    canManageConnections: false);

                await _appRegService.CreateOwnedDrive(appReg.ApplicationId, SystemAppConstants.ProfileAppFinancialProfileDriveIdentifier, "Financial Profile");
            }
        }

        private async Task SetupChat()
        {
            string chatAppName = "Chat App";

            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.ChatAppId);
            if (null == existingApp)
            {
                await _appRegService.RegisterApp(
                    SystemAppConstants.ChatAppId, 
                    chatAppName, 
                    SystemAppConstants.ChatAppDefaultDriveIdentifier,
                    createDrive: true,
                    canManageConnections: true);
            }
        }

        private async Task SetupWebHomeApp()
        {
            string webHomeAppName = "Home Page";
            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.WebHomeAppId);
            if (null == existingApp)
            {
                await _appRegService.RegisterApp(
                    SystemAppConstants.WebHomeAppId, 
                    webHomeAppName,
                    SystemAppConstants.WebHomeDefaultDriveIdentifier,
                    createDrive: true,
                    canManageConnections:false);
            }
        }
    }
}