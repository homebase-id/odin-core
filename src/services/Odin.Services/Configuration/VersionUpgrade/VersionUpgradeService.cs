using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;
using Odin.Services.Configuration.VersionUpgrade.Version1tov2;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeService(
    TenantContext tenantContext,
    TenantConfigService tenantConfigService,
    V0ToV1VersionMigrationService v1,
    V1ToV2VersionMigrationService v2,
    OwnerAuthenticationService authService,
    ILogger<VersionUpgradeService> logger)
{
    private bool _isRunning = false;

    public async Task UpgradeAsync(VersionUpgradeJobData data, CancellationToken cancellationToken)
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

        await authService.UpdateOdinContextAsync(token, clientContext, odinContext);

        var currentVersion = (await tenantConfigService.GetVersionInfoAsync()).DataVersionNumber;

        try
        {
            if (currentVersion == 0)
            {
                _isRunning = true;
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                await v1.UpgradeAsync(odinContext, cancellationToken);

                await v1.ValidateUpgradeAsync(odinContext, cancellationToken);

                currentVersion = (await tenantConfigService.IncrementVersionAsync()).DataVersionNumber;

                logger.LogInformation("Upgrading to {currentVersion} successful", currentVersion);
            }

            // do this after each version upgrade
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (currentVersion == 1)
            {
                _isRunning = true;
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                await v2.UpgradeAsync(odinContext, cancellationToken);

                await v2.ValidateUpgradeAsync(odinContext, cancellationToken);

                currentVersion = (await tenantConfigService.IncrementVersionAsync()).DataVersionNumber;

                logger.LogInformation("Upgrading to {currentVersion} successful", currentVersion);
            }

            // ...
        }
        catch (Exception ex)
        {
            await tenantConfigService.SetVersionFailureInfoAsync(currentVersion + 1);
            logger.LogInformation(ex, $"Upgrading from {currentVersion} failed.  Release Info: {ReleaseVersionInfo.BuildVersion}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public bool IsRunning()
    {
        return _isRunning;
    }
}