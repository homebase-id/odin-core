using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

public static class BaselineMigrationVersion
{
    internal static async Task ExecuteAsync(MultiTenantContainer services)
    {
        var logger = services.Resolve<ILogger<CommandLine>>();
        var config = services.Resolve<OdinConfiguration>();
        var systemDatabase = services.Resolve<SystemDatabase>();

        //
        // SYSTEM
        //
        {
            logger.LogInformation("Baselining {database}", "system");
            var migrator = services.Resolve<SystemMigrator>();
            await migrator.EnsureVersionInfoTable();
            await migrator.DANGEROUS_SetCurrentVersionAsync(0);

            logger.LogInformation("Migrating {database}", "system");
            await systemDatabase.MigrateDatabaseAsync();
        }

        //
        // IDENTITIES
        //

        var registrations = await systemDatabase.Registrations.GetAllAsync();
        foreach (var registrationRecord in registrations)
        {
            var registration = new IdentityRegistration
            {
                Id = registrationRecord.identityId,
                PrimaryDomainName = registrationRecord.primaryDomainName,
                Email = registrationRecord.email,
                FirstRunToken = string.IsNullOrEmpty(registrationRecord.firstRunToken)
                    ? null
                    : Guid.Parse(registrationRecord.firstRunToken),
                PlanId = registrationRecord.planId,
                Disabled = registrationRecord.disabled,
                MarkedForDeletionDate = registrationRecord.markedForDeletionDate,
            };

            await using var tenantScope = services.GetOrAddTenantScope(
                    registration.PrimaryDomainName,
                    cb => TenantServices.ConfigureTenantServices(cb, registration, config))
                .BeginLifetimeScope();

            Console.WriteLine();
            logger.LogInformation("Baselining {database}", registration.PrimaryDomainName);
            var migrator = tenantScope.Resolve<IdentityMigrator>();
            await migrator.EnsureVersionInfoTable();
            await migrator.DANGEROUS_SetCurrentVersionAsync(202507191211); // TableDriveMainIndexMigrationV202507191211

            logger.LogInformation("Migrating {database}", registration.PrimaryDomainName);
            var identityDatabase = tenantScope.Resolve<IdentityDatabase>();
            await identityDatabase.MigrateDatabaseAsync();
        }
    }
}