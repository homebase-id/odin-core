using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
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
                var profileDriveReq = new DriveGrantRequest()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = SystemAppConstants.ProfileAppStandardProfileDriveAlias,
                        Type = SystemAppConstants.ProfileDriveType
                    },
                    Permission = DrivePermission.All
                };
              
                var drive = await _driveService.CreateDrive("Standard Profile", profileDriveReq.Drive, "", true);

                var financialDriveReq = new DriveGrantRequest()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = SystemAppConstants.ProfileAppFinancialProfileDriveAlias,
                        Type = SystemAppConstants.ProfileDriveType
                    },
                    Permission = DrivePermission.All
                };
                
                var drive2 = await _driveService.CreateDrive("Financial Profile", financialDriveReq.Drive, "", false);

                var appReg = await _appRegService.RegisterApp(
                    SystemAppConstants.ProfileAppId,
                    profileAppName,
                    permissions: null,
                    new List<DriveGrantRequest>() { profileDriveReq, financialDriveReq });
            }
        }

        private async Task SetupChat()
        {
            string chatAppName = "Chat App";

            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.ChatAppId);
            if (null == existingApp)
            {
                var req = new DriveGrantRequest()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = SystemAppConstants.ChatAppDefaultDriveAlias,
                        Type = SystemAppConstants.ChatDriveType
                    },
                    Permission = DrivePermission.All
                };
                
                var drive = await _driveService.CreateDrive("Default Chat Drive", req.Drive, "", true);
                await _appRegService.RegisterApp(
                    SystemAppConstants.ChatAppId,
                    chatAppName,
                    permissions: null,
                    new List<DriveGrantRequest>() { req });
            }
        }

        private async Task SetupWebHomeApp()
        {
            string webHomeAppName = "Home Page";
            var existingApp = await _appRegService.GetAppRegistration(SystemAppConstants.WebHomeAppId);
            if (null == existingApp)
            {
                var req = new DriveGrantRequest()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = SystemAppConstants.WebHomeDefaultDriveAlias,
                        Type = SystemAppConstants.WebHomeDriveType
                    },
                    Permission = DrivePermission.All
                };
                
                var drive = await _driveService.CreateDrive("Web Home Default Drive", req.Drive, "", true);
                
                await _appRegService.RegisterApp(
                    SystemAppConstants.WebHomeAppId,
                    webHomeAppName,
                    permissions: null,
                    new List<DriveGrantRequest>() { req });
            }
        }
    }
}