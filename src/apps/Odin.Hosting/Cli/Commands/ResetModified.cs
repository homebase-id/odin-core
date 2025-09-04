using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Tasks;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

namespace Odin.Hosting.Cli.Commands;

public static class ResetModified
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();

        {
            var db = services.GetRequiredService<SystemDatabase>();
            await using var cn = await db.CreateScopedConnectionAsync();
            var sql =
                """
                UPDATE Jobs SET modified = created WHERE modified IS NULL;
                UPDATE Certificates SET modified = created WHERE modified IS NULL;
                UPDATE Registrations SET modified = created WHERE modified IS NULL;
                UPDATE Settings SET modified = created WHERE modified IS NULL;
                """;
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            var rows = await cmd.ExecuteNonQueryAsync();
            logger.LogInformation("Updated {rows} rows for system", rows);
        }

        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        registry.LoadRegistrations().BlockingWait();
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var db = scope.Resolve<IdentityDatabase>();
            await using var cn = await db.CreateScopedConnectionAsync();
            var sql =
                """
                UPDATE Drives SET modified = created WHERE modified IS NULL;
                UPDATE DriveMainIndex SET modified = created WHERE modified IS NULL;
                UPDATE AppNotifications SET modified = created WHERE modified IS NULL;
                UPDATE Connections SET modified = created WHERE modified IS NULL;
                UPDATE ImFollowing SET modified = created WHERE modified IS NULL;
                UPDATE FollowsMe SET modified = created WHERE modified IS NULL;
                UPDATE Inbox SET modified = created WHERE modified IS NULL;
                UPDATE Outbox SET modified = created WHERE modified IS NULL;
                UPDATE Nonce SET modified = created WHERE modified IS NULL;
                """;
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            var rows = await cmd.ExecuteNonQueryAsync();
            logger.LogInformation("Updated {rows} rows for tenant {t}", rows, tenant.PrimaryDomainName);
        }

    }
}