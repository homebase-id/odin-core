using System;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;

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
            await SetupLandingPage();
            await SetupChat();
        }

        private async Task SetupProfile()
        {
            Guid profileAppId = Guid.Parse("99999789-4444-4444-4444-000000004444");
            string profileAppName = "Profile Data";

            var existingApp = await _appRegService.GetAppRegistration(profileAppId);
            if (null == existingApp)
            {
                await _appRegService.RegisterApp(profileAppId, profileAppName, createDrive: true, canManageConnections: false);
            }
        }

        private async Task SetupChat()
        {
            Guid chatAppId = Guid.Parse("99999789-5555-5555-5555-000000002222");
            string chatAppName = "Chat App";

            var existingApp = await _appRegService.GetAppRegistration(chatAppId);
            if (null == existingApp)
            {
                await _appRegService.RegisterApp(chatAppId, chatAppName, createDrive: true, canManageConnections: true);
            }
        }

        private async Task SetupLandingPage()
        {
            Guid landingPageAppId = Guid.Parse("99999789-6666-6666-6666-000000001111");
            string landingPageAppName = "Landing Page";

            await _appRegService.RegisterApp(landingPageAppId, landingPageAppName, true);
        }
    }
}