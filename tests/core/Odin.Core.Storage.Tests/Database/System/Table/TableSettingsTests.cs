using System.Linq;
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

public class TableSettingsBumpMonotonicTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task BumpReportsTheValueItAdvancedFrom(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var settings = scope.Resolve<TableSettings>();

        var (p1, c1) = await settings.BumpMonotonicAsync("counter");
        var (p2, c2) = await settings.BumpMonotonicAsync("counter");
        var (p3, c3) = await settings.BumpMonotonicAsync("counter");

        Assert.That(c1, Is.GreaterThan(p1), "first bump must advance");
        Assert.That(p2, Is.EqualTo(c1), "second bump must report the first bump's result as its starting point");
        Assert.That(p3, Is.EqualTo(c2), "third bump must report the second bump's result as its starting point");
        Assert.That(c3, Is.GreaterThan(c2).And.GreaterThan(c1), "strictly monotonic");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ConcurrentBumpsFormOneUnbrokenChain(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        const int bumps = 40;
        var results = new global::System.Collections.Concurrent.ConcurrentBag<(long previous, long current)>();

        // Each task gets its own scope (and connection), so bumps genuinely contend on the row.
        await Task.WhenAll(Enumerable.Range(0, bumps).Select(async _ =>
        {
            await using var scope = Services.BeginLifetimeScope();
            var settings = scope.Resolve<TableSettings>();
            results.Add(await settings.BumpMonotonicAsync("counter"));
        }));

        var ordered = results.OrderBy(r => r.current).ToList();
        Assert.That(ordered.Select(r => r.current).Distinct().Count(), Is.EqualTo(bumps), "every bump got its own value");
        for (var i = 1; i < ordered.Count; i++)
        {
            // If two bumps had read the same "previous", one of them would break this chain, which is
            // exactly the interleaving the registry relies on being impossible.
            Assert.That(ordered[i].previous, Is.EqualTo(ordered[i - 1].current),
                $"bump {i} advanced from {ordered[i].previous} but the previous bump produced {ordered[i - 1].current}");
        }
    }
}
