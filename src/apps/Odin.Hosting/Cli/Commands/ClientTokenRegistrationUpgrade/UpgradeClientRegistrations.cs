using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands.ClientTokenRegistrationUpgrade;

public static class UpgradeClientRegistrations
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        await registry.LoadRegistrations();
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            var odinId = (OdinId)tenant.PrimaryDomainName;
            var scope = tenantContainer.GetTenantScope(odinId);
            // var tenantConfig = scope.Resolve<TenantConfigService>();
            // var versionInfo = await tenantConfig.GetVersionInfoAsync();

            var kittyConverter = new ClientTokenRegistrationConverter(
                scope.Resolve<ILogger<ClientTokenRegistrationConverter>>(),
                scope.Resolve<ClientRegistrationStorage>(),
                scope.Resolve<TableKeyThreeValueCached>(),
                scope.Resolve<TableKeyValue>()
            );

            await kittyConverter.UpdateAppClientRegistration(odinId);

            await kittyConverter.UpdateOwnerClientRegistration(odinId);
        }
    }
}