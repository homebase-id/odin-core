using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeService(
    TenantContext tenantContext,
    TenantConfigService tenantConfigService,
    V0ToV1VersionMigrationService v0ToV1VersionMigrationService,
    OwnerAuthenticationService authService,
    ILogger<VersionUpgradeService> logger)
{
    public async Task Upgrade(VersionUpgradeJobData data)
    {
        logger.LogInformation($"Running Version Upgrade Process for {data.Tenant}");

        var tokenBytes = AesCbc.Decrypt(data.EncryptedToken, tenantContext.TemporalEncryptionKey, data.Iv);
        var token = ClientAuthenticationToken.FromPortableBytes(tokenBytes);

        var odinContext = new OdinContext
        {
            Tenant = default,
            AuthTokenCreated = null,
            Caller = null
        };

        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContext(token, clientContext, odinContext);

        var currentVersion = tenantConfigService.GetVersionInfo().DataVersionNumber;

        try
        {
            if (currentVersion == 0)
            {
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                await v0ToV1VersionMigrationService.Upgrade(odinContext);
                currentVersion = tenantConfigService.IncrementVersion().DataVersionNumber;

                logger.LogInformation("Upgrading to {currentVersion} successful", currentVersion);
            }

            // if (currentVersion == 1)
            // {
            //     logger.LogInformation("Upgrading from {currentVersion}", currentVersion);
            //
            //     // do something else
            //     _ = tenantConfigService.IncrementVersion().DataVersionNumber;
            // }

            // ...
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Upgrading from {currentVersion} failed");
        }

        await Task.CompletedTask;
    }
}