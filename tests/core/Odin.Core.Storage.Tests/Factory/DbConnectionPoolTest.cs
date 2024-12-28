using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Factory;

public class DbConnectionPoolTest : IocTestBase
{
    [Test]
    public async Task PoolCountersShouldMatch()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite); // PG not using DbConnectionPool

        //
        // Don't do below in real code, it's just for testing purposes and is bending the rules
        //

        const int connectionCount = 1000;
        const int stackCount = 100;
        var scopes = new List<ILifetimeScope>();
        var connections = new List<IConnectionWrapper>();

        var pool = Services.Resolve<IDbConnectionPool>();
        await pool.ClearAllAsync();

        var counters = Services.Resolve<DatabaseCounters>();
        counters.Reset();

        for (var i = 0; i < connectionCount; i++)
        {
            var scope = Services.BeginLifetimeScope();
            scopes.Add(scope);
            var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

            for (var j = 0; j < stackCount; j++)
            {
                var connection = await scopedConnectionFactory.CreateScopedConnectionAsync();
                connections.Add(connection);
            }
        }

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }

        foreach (var scope in scopes)
        {
            await scope.DisposeAsync();
        }

        Assert.AreEqual(connectionCount, counters.NoDbOpened);
        Assert.AreEqual(connectionCount, counters.NoDbClosed);

        Assert.AreEqual(connectionCount, counters.NoPoolOpened);
        Assert.AreEqual(connectionCount - pool.PoolSize, counters.NoPoolClosed);

        await pool.ClearAllAsync();
        Assert.AreEqual(connectionCount, counters.NoPoolClosed);
    }

}