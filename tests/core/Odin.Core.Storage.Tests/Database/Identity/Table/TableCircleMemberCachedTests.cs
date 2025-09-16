using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableCircleMemberCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableCircleMemberCached = scope.Resolve<TableCircleMemberCached>();

        var c1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var m1 = Guid.Parse("11111111-BBBB-0000-0000-000000000000");
        var d1 = Guid.Parse("11111111-DDDD-0000-0000-000000000000").ToByteArray();
        var c2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var m2 = Guid.Parse("22222222-BBBB-0000-0000-000000000000");
        var d2 = Guid.Parse("22222222-DDDD-0000-0000-000000000000").ToByteArray();
        var c3 = Guid.Parse("33333333-AAAA-0000-0000-000000000000");
        var m3 = Guid.Parse("33333333-BBBB-0000-0000-000000000000");
        var d3 = Guid.Parse("33333333-DDDD-0000-0000-000000000000").ToByteArray();

        {
            var record = await tableCircleMemberCached.GetCircleMembersAsync(c1, TimeSpan.FromMilliseconds(100));
            Assert.That(record.Count, Is.EqualTo(0));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(0));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(1));
        }

        {
            var record = await tableCircleMemberCached.GetCircleMembersAsync(c1, TimeSpan.FromMilliseconds(100));
            Assert.That(record.Count, Is.EqualTo(0));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(1));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(1));
        }

        {
            var record = await tableCircleMemberCached.GetMemberCirclesAndDataAsync(m1, TimeSpan.FromMilliseconds(100));
            Assert.That(record.Count, Is.EqualTo(0));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(1));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(2));
        }

        {
            var record = await tableCircleMemberCached.GetMemberCirclesAndDataAsync(m1, TimeSpan.FromMilliseconds(100));
            Assert.That(record.Count, Is.EqualTo(0));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(2));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(2));
        }

        await tableCircleMemberCached.InsertAsync(
            new CircleMemberRecord { circleId = c1, memberId = m1, data = d1 });

        await tableCircleMemberCached.InsertAsync(
            new CircleMemberRecord { circleId = c1, memberId = m2, data = d2 });

        await tableCircleMemberCached.InsertAsync(
            new CircleMemberRecord { circleId = c3, memberId = m3, data = d3 });

        {
            var records = await tableCircleMemberCached.GetAllCirclesAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(3));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(2));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(3));
        }

        {
            var records = await tableCircleMemberCached.GetCircleMembersAsync(c1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(2));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(4));
        }

        {
            var records = await tableCircleMemberCached.GetCircleMembersAsync(c1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(3));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(4));
        }

        {
            var records = await tableCircleMemberCached.GetMemberCirclesAndDataAsync(m1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(3));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(5));
        }

        {
            var records = await tableCircleMemberCached.GetMemberCirclesAndDataAsync(m1, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableCircleMemberCached.Hits, Is.EqualTo(4));
            Assert.That(tableCircleMemberCached.Misses, Is.EqualTo(5));
        }
    }

    //

}


