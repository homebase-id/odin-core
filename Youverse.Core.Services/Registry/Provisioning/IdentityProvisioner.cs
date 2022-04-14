using System;
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
                    SystemAppConstants.ProfileAppStandardProfileDriveAlias,
                    driveType: SystemAppConstants.ProfileDriveType,
                    driveName: "Standard Profile",
                    driveMetadata: "",
                    createDrive: true,
                    canManageConnections: false,
                    allowAnonymousReadsToDrive: true);

                // await _appRegService.CreateOwnedDrive(
                //     appReg.ApplicationId,
                //     SystemAppConstants.ProfileAppStandardProfileDriveAlias,
                //     "Standard Profile",
                //     type: SystemAppConstants.ProfileDriveType,
                //     metadata: "",
                //     allowAnonymousReads: true);

                await _appRegService.CreateOwnedDrive(
                    appReg.ApplicationId,
                    SystemAppConstants.ProfileAppFinancialProfileDriveAlias,
                    "Financial Profile",
                    type: SystemAppConstants.ProfileDriveType,
                    metadata: "",
                    allowAnonymousReads: false);
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
                    SystemAppConstants.ChatAppDefaultDriveAlias,
                    driveType: SystemAppConstants.ChatDriveType,
                    driveName:"Default Chat Drive",
                    driveMetadata: "",
                    createDrive: true,
                    canManageConnections: true,
                    allowAnonymousReadsToDrive: false);
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
                    SystemAppConstants.WebHomeDefaultDriveAlias,
                    driveType: SystemAppConstants.WebHomeDriveType,
                    driveName:"Web Home Default Drive",
                    driveMetadata: "",
                    createDrive: true,
                    canManageConnections: false,
                    allowAnonymousReadsToDrive: true);
            }
        }
    }
}