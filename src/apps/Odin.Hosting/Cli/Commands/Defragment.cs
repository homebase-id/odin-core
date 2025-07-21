using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Tasks;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

public static class Defragment
{
    internal static async Task ExecuteAsync(IServiceProvider services, bool cleanup)
    {
        var config = services.GetRequiredService<OdinConfiguration>();
        if (config.S3PayloadStorage.Enabled)
        {
            throw new OdinSystemException("S3 defragmentation is not supported");
        }

        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        logger.LogInformation("Starting defragmentation; cleanup mode: {cleanup}", cleanup);

        registry.LoadRegistrations().BlockingWait();
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var defragmenter = scope.Resolve<Services.Drives.DriveCore.Storage.Defragmenter>();

            logger.LogInformation("Defragmenting {tenant}", tenant.PrimaryDomainName);
            await defragmenter.Defragment(cleanup);
        }
    }
}