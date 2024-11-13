using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

public class IdentityDatabase(IServiceProvider services)
{
    //
    // Put all database tables alphabetically here:
    //
    public static readonly ImmutableList<Type> TableTypes = [
        // typeof(TableAppGrants)
    ];

    //
    // Convenience properties
    //
    // public TableAppGrants Jobs => services.GetRequiredService<TableAppGrants>();
    // ...
    // ...

    //
    // Migration
    //

    // SEB:NOTE this is temporary until we have a proper migration system
    public async Task CreateDatabaseAsync(bool dropExistingTables)
    {
        using var scope = services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        foreach (var tableType in TableTypes)
        {
            var table = (ITableMigrator)scope.ServiceProvider.GetRequiredService(tableType);
            await table.EnsureTableExistsAsync(dropExistingTables);
        }
        await tx.CommitAsync();
    }

}