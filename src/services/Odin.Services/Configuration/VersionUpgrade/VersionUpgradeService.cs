using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeService(TenantConfigService tenantConfigService, ILogger<VersionUpgradeService> logger)
{
    public SensitiveByteArray TemporalEncryptionKey { get; } = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

    public async Task Upgrade(VersionUpgradeJobData data)
    {
        var odinContextBytes = AesCbc.Decrypt(data.EncryptedOdinContextData, TemporalEncryptionKey, data.Iv);
        var odinContext = OdinSystemSerializer.DeserializeOrThrow<OdinContext>(odinContextBytes.ToStringFromUtf8Bytes());

        var currentVersion = tenantConfigService.GetVersionInfo().DataVersionNumber;

        try
        {
            if (currentVersion == 0)
            {
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                // prepare introductions
                currentVersion = tenantConfigService.IncrementVersion().DataVersionNumber;
            }

            if (currentVersion == 1)
            {
                logger.LogInformation("Upgrading from {currentVersion}", currentVersion);

                // do something else
                _ = tenantConfigService.IncrementVersion().DataVersionNumber;
            }

            // ...
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured");
        }
        finally
        {
        }

        await Task.CompletedTask;
    }
}