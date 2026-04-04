using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.System.Table;

public class TableSettingsTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldPageByRowId(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var tableSettings = scope.Resolve<TableSettings>();

        for (int i = 0; i < 3; i++)
        {
            await tableSettings.InsertAsync(new SettingsRecord
            {
                key = $"key{i}",
                value = $"value{i}",
            });
        }

        var (page1, cursor1) = await tableSettings.PagingByRowIdAsync(2, null);
        Assert.That(page1.Count, Is.EqualTo(2));
        Assert.That(cursor1, Is.Not.Null);

        var (page2, cursor2) = await tableSettings.PagingByRowIdAsync(2, cursor1);
        Assert.That(page2.Count, Is.EqualTo(1));
        Assert.That(cursor2, Is.Null);

        var (all, allCursor) = await tableSettings.PagingByRowIdAsync(100, null);
        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(allCursor, Is.Null);
    }
}
