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

        public async Task ConfigureIdentityDefaults()
        {
            await SetupChat();
            await SetupLandingPage();
        }

        private async Task SetupChat()
        {
            Guid chatAppId = Guid.Parse("99999789-5555-5555-5555-000000002222");
            string chatAppName = "Chat App";
            byte[] encryptedSharedSecret = Guid.Empty.ToByteArray();

            await _appRegService.RegisterApp(chatAppId, chatAppName, encryptedSharedSecret, true);
        }

        private async Task SetupLandingPage()
        {
            Guid landingPageAppId = Guid.Parse("99999789-5555-5555-5555-000000001111");
            string landingPageAppName = "Landing Page";
            byte[] encryptedSharedSecret = Guid.Empty.ToByteArray();

            await _appRegService.RegisterApp(landingPageAppId, landingPageAppName, encryptedSharedSecret, true);
        }
    }
}