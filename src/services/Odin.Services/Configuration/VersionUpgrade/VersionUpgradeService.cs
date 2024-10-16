using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeService(TenantConfigService tenantConfigService,
    V0ToV1VersionMigrationService v0ToV1VersionMigrationService,
    ILogger<VersionUpgradeService> logger)
{
    public SensitiveByteArray TemporalEncryptionKey { get; } = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

    public async Task Upgrade(VersionUpgradeJobData data)
    {
        logger.LogInformation("Running Version Upgrade Process");

        var odinContextBytes = AesCbc.Decrypt(data.EncryptedOdinContextData, TemporalEncryptionKey, data.Iv);
        var odinContext = OdinSystemSerializer.DeserializeOrThrow<OdinContext>(odinContextBytes.ToStringFromUtf8Bytes());

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
            logger.LogError(ex, "Unhandled exception occured");
        }

        await Task.CompletedTask;
    }
}