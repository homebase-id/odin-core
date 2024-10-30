using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Database.Connection;
using Odin.Core.Storage.Database.Connection.System;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.Database.Connection;

public class ScopedConnectionFactoryTest
{
    [Test]
    public async Task TestMethod1()
    {
        // IMPORTANT: "scope" below means a "scope" in the context of a DI container.

        // Included for completeness:
        var serviceProvider = new ServiceContainer();

        // Included for completeness. Normally handled by the DI container.
        using var scope = serviceProvider.CreateScope();

        // Included for completeness. Will normally be injected where ever a connection is needed.
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedConnectionFactory>();

        // Connections are "shared" in the same scope. This is required for transactions.
        await using var connection = await scopedConnectionFactory.CreateScopedConnectionAsync();

        // Transactions are "shared" in the same scope. This is required for nested transactions.
        await using var transaction = await connection.BeginNestedTransactionAsync();

        //
        // At this point, anything ON THE SAME SCOPE that calls CreateScopedConnectionAsync will get the same connection
        // (and transaction) as the one created above. This includes any class that is injected directly or indirectly
        // with ScopedConnectionFactory.
        //

        await using var cmd = connection.Instance.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteNonQueryAsync();

        // Outermost explicit commit is required transactions, otherwise the transaction will be rolled back.
        await transaction.CommitAsync();



        Assert.Pass();


        // Act

        // Assert
    }

}