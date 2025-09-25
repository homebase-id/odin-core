using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

public static class LogTenantVersions
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {

        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        var logger = services.GetRequiredService<ILogger<CommandLine>>();

        await registry.LoadRegistrations();
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantConfig = scope.Resolve<TenantConfigService>();
            var versionInfo = await tenantConfig.GetVersionInfoAsync();
            logger.LogInformation("Tenant: {tenantId} version: {version} lastUpgrade: {lastUpgrade}",
                tenant.PrimaryDomainName,
                versionInfo.DataVersionNumber,
                DateTimeOffset.FromUnixTimeMilliseconds(versionInfo.LastUpgraded.milliseconds).ToString("O"));
        }
    }

}