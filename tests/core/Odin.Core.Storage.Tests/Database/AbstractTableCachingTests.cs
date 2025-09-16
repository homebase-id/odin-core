using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database;

public class AbstractTableCachingTests : IocTestBase
{
    [Test]
    public async Task ItShouldNotBlowUpWhileDoingDeferredCommitActions()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            var tableAppNotificationsCached = scope.Resolve<TableAppNotificationsCached>();
            await tableAppNotificationsCached.DeleteAsync(Guid.NewGuid());
            await tableAppNotificationsCached.DeleteAsync(Guid.NewGuid());

            var tableAppGrantsCached = scope.Resolve<TableAppGrantsCached>();
            await tableAppGrantsCached.DeleteByIdentityAsync(Guid.NewGuid());
            await tableAppGrantsCached.DeleteByIdentityAsync(Guid.NewGuid());

            var tableKeyValueCached = scope.Resolve<TableKeyValueCached>();
            var k1 = Guid.NewGuid().ToByteArray();
            await tableKeyValueCached.DeleteAsync(k1);
            await tableKeyValueCached.DeleteAsync(k1);

            tx.Commit();
        }

        Assert.Pass();
    }

    //

}

