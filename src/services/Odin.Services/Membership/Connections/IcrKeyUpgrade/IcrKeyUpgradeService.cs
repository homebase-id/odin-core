using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Membership.Connections.IcrKeyUpgrade;

public class IcrKeyUpgradeService(
    TenantConfigService tenantConfigService,
    OwnerAuthenticationService authService,
    IAppRegistrationService appRegService,
    CircleNetworkIntroductionService circleNetworkIntroductionService,
    TenantContext tenantContext,
    CircleNetworkService circleNetworkService,
    ILogger<IcrKeyUpgradeService> logger)
{
    public async Task Upgrade(IcrKeyUpgradeJobData data)
    {
        logger.LogInformation("Running Version Upgrade Process");

        var odinContext = await GetOdinContext(data);
        var currentVersion = tenantConfigService.GetVersionInfo().DataVersionNumber;

        try
        {
            await Run(odinContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Upgrading from {currentVersion} failed");
        }

        await Task.CompletedTask;
    }

    private async Task<IOdinContext> GetOdinContext(IcrKeyUpgradeJobData data)
    {
        if (string.IsNullOrEmpty(data.Tenant))
        {
            throw new OdinSystemException("Tenant not specified");
        }

        var tokenBytes = AesCbc.Decrypt(data.EncryptedToken, tenantContext.TemporalEncryptionKey, data.Iv);
        var token = ClientAuthenticationToken.FromPortableBytes(tokenBytes);

        switch (data.TokenType)
        {
            case IcrKeyUpgradeJobData.JobTokenType.App:
                return await LoadFromApp(token, data.Tenant.Value);

            case IcrKeyUpgradeJobData.JobTokenType.Owner:
                return await LoadFromOwner(token, data.Tenant.Value);
        }

        throw new NotImplementedException("Unknown token type");
    }

    private async Task<IOdinContext> LoadFromApp(ClientAuthenticationToken token, OdinId tenant)
    {
        var odinContext = new OdinContext
        {
            Tenant = tenant,
            AuthTokenCreated = null,
            Caller = null
        };

        var ctx = await appRegService.GetAppPermissionContext(token, odinContext);
        return ctx;
    }

    private async Task<IOdinContext> LoadFromOwner(ClientAuthenticationToken token, OdinId tenant)
    {
        var odinContext = new OdinContext
        {
            Tenant = tenant,
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
        return odinContext;
    }

    private async Task Run(IOdinContext odinContext)
    {
        try
        {
            if (!tenantContext.Settings.DisableAutoAcceptIntroductions &&
                odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
            {
                await circleNetworkIntroductionService.AutoAcceptEligibleConnectionRequests(odinContext);
            }

            if (odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnectionRequests))
            {
                await circleNetworkIntroductionService.SendOutstandingConnectionRequests(odinContext);
            }

            if (odinContext.PermissionsContext.HasPermission(PermissionKeys.ReadConnections))
            {
                await circleNetworkService.UpgradeWeakClientAccessTokens(odinContext);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured");
        }
    }
}