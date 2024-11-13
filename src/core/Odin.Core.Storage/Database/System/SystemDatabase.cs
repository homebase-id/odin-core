using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System;

public class SystemDatabase(ILifetimeScope lifetimeScope)
{
    //
    // Put all database tables alphabetically here:
    //
    public static readonly ImmutableList<Type> TableTypes = [
        typeof(TableJobs)
    ];

    //
    // Convenience properties
    //
    public TableJobs Jobs => lifetimeScope.Resolve<TableJobs>();

    //
    // Migration
    //

    // SEB:NOTE this is temporary until we have a proper migration system
    public async Task CreateDatabaseAsync(bool dropExistingTables = false)
    {
        await using var scope = lifetimeScope.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        foreach (var tableType in TableTypes)
        {
            var table = (ITableMigrator)scope.Resolve(tableType);
            await table.EnsureTableExistsAsync(dropExistingTables);
        }
        await tx.CommitAsync();
    }

}