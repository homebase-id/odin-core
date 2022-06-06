using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Registry.Provisioning
{
    public class IdentityProvisioner : IIdentityProvisioner
    {
        private readonly IAppRegistrationService _appRegService;
        private readonly IDriveService _driveService;

        public IdentityProvisioner(IAppRegistrationService appRegService, IDriveService driveService)
        {
            _appRegService = appRegService;
            _driveService = driveService;
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
                var profileTargetDrive = new TargetDrive()
                {
                    Alias = SystemAppConstants.ProfileAppStandardProfileDriveAlias,
                    Type = SystemAppConstants.ProfileDriveType
                };

                var drive = await _driveService.CreateDrive("Standard Profile", profileTargetDrive, "", true);

                var financialTargetDrive = new TargetDrive()
                {
                    Alias = SystemAppConstants.ProfileAppFinancialProfileDriveAlias,
                    Type = SystemAppConstants.ProfileDriveType
                };
                
                var drive2 = await _driveService.CreateDrive("Financial Profile", financialTargetDrive, "", false);

                var appReg = await _appRegService.RegisterApp(
                    SystemAppConstants.ProfileAppId,
                    profileAppName,
                    permissions: null,
                    new List<Guid>() { drive.Id, drive2.Id });
            }
        }

        private async Task SetupChat()
        {
            string chatAppName = "Chat App";

            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.ChatAppId);
            if (null == existingApp)
            {
                var targetDrive = new TargetDrive()
                {
                    Alias = SystemAppConstants.ChatAppDefaultDriveAlias,
                    Type = SystemAppConstants.ChatDriveType
                };

                var drive = await _driveService.CreateDrive("Default Chat Drive", targetDrive, "", true);
                await _appRegService.RegisterApp(
                    SystemAppConstants.ChatAppId,
                    chatAppName,
                    permissions: null,
                    new List<Guid>() { drive.Id });
            }
        }

        private async Task SetupWebHomeApp()
        {
            string webHomeAppName = "Home Page";
            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.WebHomeAppId);
            if (null == existingApp)
            {
                var targetDrive = new TargetDrive()
                {
                    Alias = SystemAppConstants.WebHomeDefaultDriveAlias,
                    Type = SystemAppConstants.WebHomeDriveType
                };
                var drive = await _driveService.CreateDrive("Web Home Default Drive", targetDrive, "", true);
                await _appRegService.RegisterApp(
                    SystemAppConstants.WebHomeAppId,
                    webHomeAppName,
                    permissions: null,
                    new List<Guid>() { drive.Id });
            }
        }
    }
}