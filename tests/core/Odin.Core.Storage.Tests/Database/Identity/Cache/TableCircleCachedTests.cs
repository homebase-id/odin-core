using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableCircleCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableCircleCached = scope.Resolve<TableCircleCached>();

        var circleId1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var circleName1 = "circle1";
        var circleData1 = Guid.Parse("11111111-DDDD-0000-0000-000000000000").ToByteArray();
        var circleId2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var circleName2 = "circle2";
        var circleData2 = Guid.Parse("22222222-DDDD-0000-0000-000000000000").ToByteArray();
        var circleId3 = Guid.Parse("33333333-AAAA-0000-0000-000000000000");
        var circleName3 = "circle3";
        var circleData3 = Guid.Parse("33333333-DDDD-0000-0000-000000000000").ToByteArray();

        {
            var record = await tableCircleCached.GetAsync(circleId1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableCircleCached.Hits, Is.EqualTo(0));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(1));
        }

        await tableCircleCached.InsertAsync(
            new CircleRecord { circleId = circleId1, circleName = circleName1, data = circleData1 });
        await tableCircleCached.InsertAsync(
            new CircleRecord { circleId = circleId2, circleName = circleName2, data = circleData2 });
        await tableCircleCached.InsertAsync(
            new CircleRecord { circleId = circleId3, circleName = circleName3, data = circleData3 });

        {
            var record = await tableCircleCached.GetAsync(circleId1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableCircleCached.Hits, Is.EqualTo(0));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(2));
        }


        {
            List<CircleRecord> records;
            Guid? cursor;

            // Record 1 MISS
            (records, _) = await tableCircleCached.PagingByCircleIdAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].circleId, Is.EqualTo(circleId1));
            Assert.That(tableCircleCached.Hits, Is.EqualTo(0));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(3));

            // Record 1 HIT
            (records, cursor) = await tableCircleCached.PagingByCircleIdAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].circleId, Is.EqualTo(circleId1));
            Assert.That(tableCircleCached.Hits, Is.EqualTo(1));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(3));

            // Record 1 MISS
            (records, _) = await tableCircleCached.PagingByCircleIdAsync(1, cursor, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].circleId, Is.EqualTo(circleId2));
            Assert.That(tableCircleCached.Hits, Is.EqualTo(1));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(4));

            // Record 1 HIT
            (records, cursor) = await tableCircleCached.PagingByCircleIdAsync(1, cursor, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].circleId, Is.EqualTo(circleId2));
            Assert.That(tableCircleCached.Hits, Is.EqualTo(2));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(4));
        }

        await tableCircleCached.DeleteAsync(circleId3);

        {
            var record = await tableCircleCached.GetAsync(circleId3, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableCircleCached.Hits, Is.EqualTo(2));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(5));
        }

        {
            List<CircleRecord> records;

            // Record 1 MISS - DeleteAsync invalidated all Paging caches
            (records, _) = await tableCircleCached.PagingByCircleIdAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].circleId, Is.EqualTo(circleId1));
            Assert.That(tableCircleCached.Hits, Is.EqualTo(2));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(6));

            // Record 1 HIT
            (records, _) = await tableCircleCached.PagingByCircleIdAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].circleId, Is.EqualTo(circleId1));
            Assert.That(tableCircleCached.Hits, Is.EqualTo(3));
            Assert.That(tableCircleCached.Misses, Is.EqualTo(6));
        }
    }

    //

}


