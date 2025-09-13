using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableAppGrantsCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldInsert()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableAppGrantsCached = scope.Resolve<TableAppGrantsCached>();

        var appId = SequentialGuid.CreateGuid();
        var data = Guid.NewGuid().ToByteArray();
        var circleId = SequentialGuid.CreateGuid();
        var odinHashId = SequentialGuid.CreateGuid();

        var i = await tableAppGrantsCached.InsertAsync(
            new AppGrantsRecord { appId = appId, circleId = circleId, data = data, odinHashId = odinHashId });

        Assert.That(i, Is.EqualTo(1));
        Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(0));
        Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(0));
    }

    //

    [Test]
    public async Task ItShouldGetAll_ThenUpdate_ThenInvalidateAll()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableAppGrantsCached = scope.Resolve<TableAppGrantsCached>();

        var appId1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var circleId1 = Guid.Parse("11111111-BBBB-0000-0000-000000000000");
        var odinHashId1 = Guid.Parse("11111111-CCCC-0000-0000-000000000000");
        var data1 = Guid.Parse( "11111111-DDDD-0000-0000-000000000000").ToByteArray();

        var appId2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var circleId2 = Guid.Parse("22222222-BBBB-0000-0000-000000000000");
        var odinHashId2 = Guid.Parse("22222222-CCCC-0000-0000-000000000000");
        var data2 = Guid.Parse( "22222222-DDDD-0000-0000-000000000000").ToByteArray();

        await tableAppGrantsCached.InsertAsync(
            new AppGrantsRecord { appId = appId1, circleId = circleId1, data = data1, odinHashId = odinHashId1 });

        await tableAppGrantsCached.InsertAsync(
            new AppGrantsRecord { appId = appId2, circleId = circleId2, data = data2, odinHashId = odinHashId2 });

        Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(0));
        Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(0));

        // Single record miss
        {
            var records = await tableAppGrantsCached.GetByOdinHashIdAsync(odinHashId1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].data, Is.EqualTo(data1));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(0));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(1));
        }

        // Single record hit
        {
            var records = await tableAppGrantsCached.GetByOdinHashIdAsync(odinHashId1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].data, Is.EqualTo(data1));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(1));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(1));
        }

        // All records miss
        {
            var all = await tableAppGrantsCached.GetAllAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(1));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(2));
        }

        // All records hit
        {
            var all = await tableAppGrantsCached.GetAllAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(2));
        }

        await tableAppGrantsCached.UpsertAsync(
            new AppGrantsRecord { appId = appId1, circleId = circleId1, data = data2, odinHashId = odinHashId1 });

        // Single record miss (upsert has invalidated GetByOdinHashIdAsync)
        {
            var records = await tableAppGrantsCached.GetByOdinHashIdAsync(odinHashId1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].data, Is.EqualTo(data2));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(3));
        }

        // Single record hit
        {
            var records = await tableAppGrantsCached.GetByOdinHashIdAsync(odinHashId1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].data, Is.EqualTo(data2));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(3));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(3));
        }

        await tableAppGrantsCached.DeleteByIdentityAsync(odinHashId1);

        // All records miss (delete has invalidated everything)
        {
            var all = await tableAppGrantsCached.GetAllAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].odinHashId, Is.EqualTo(odinHashId2));
            Assert.That(all[0].data, Is.EqualTo(data2));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(3));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(4));
        }

        // All records hit
        {
            var all = await tableAppGrantsCached.GetAllAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].odinHashId, Is.EqualTo(odinHashId2));
            Assert.That(all[0].data, Is.EqualTo(data2));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(4));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(4));
        }

        await tableAppGrantsCached.InsertAsync(
            new AppGrantsRecord { appId = appId1, circleId = circleId1, data = data1, odinHashId = odinHashId1 });

        await tableAppGrantsCached.InvalidateAllAsync();

        // Single record miss
        {
            var records = await tableAppGrantsCached.GetByOdinHashIdAsync(odinHashId1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(4));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(5));
        }

        // Single record miss
        {
            var records = await tableAppGrantsCached.GetByOdinHashIdAsync(odinHashId2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableAppGrantsCached.Hits, Is.EqualTo(4));
            Assert.That(tableAppGrantsCached.Misses, Is.EqualTo(6));
        }
    }

}


