using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Tasks;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

public static class JsonRegToDb
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();
        var logger = services.GetRequiredService<ILogger<CommandLine>>();

        //
        // Migrate
        //

        registry.LoadRegistrations().BlockingWait();
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Migrating registrations for tenant: {tenant}", tenant.PrimaryDomainName);

            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tableRegistrations = scope.Resolve<TableRegistrations>();
            var registrationsRecord = new RegistrationsRecord
            {
                identityId = tenant.Id,
                primaryDomainName = tenant.PrimaryDomainName.ToLower(),
                email = tenant.Email?.ToLower(),
                firstRunToken = tenant.FirstRunToken?.ToString(),
                disabled = tenant.Disabled,
                markedForDeletionDate = tenant.MarkedForDeletionDate,
                planId = tenant.PlanId,
            };
            tableRegistrations.UpsertAsync(registrationsRecord).BlockingWait();
        }

        //
        // Verify
        //

        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);

            var tableRegistrations = scope.Resolve<TableRegistrations>();
            var registration = tableRegistrations.GetAsync(tenant.Id);
            if (registration == null)
            {
                throw new Exception($"Registration {tenant.Id} not found");
            }
        }
    }
}
