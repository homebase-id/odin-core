using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableClientRegistrationsTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task PagingByRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableClientRegistrations>();

            var catId1 = SequentialGuid.CreateGuid();
            var catId2 = SequentialGuid.CreateGuid();
            var catId3 = SequentialGuid.CreateGuid();
            var categoryId = SequentialGuid.CreateGuid();

            await tbl.UpsertAsync(new ClientRegistrationsRecord() { catId = catId1, issuedToId = "client1", ttl = 3600, expiresAt = new UnixTimeUtc(0), categoryId = categoryId, catType = 1, value = null });
            await tbl.UpsertAsync(new ClientRegistrationsRecord() { catId = catId2, issuedToId = "client2", ttl = 3600, expiresAt = new UnixTimeUtc(0), categoryId = categoryId, catType = 1, value = null });
            await tbl.UpsertAsync(new ClientRegistrationsRecord() { catId = catId3, issuedToId = "client3", ttl = 3600, expiresAt = new UnixTimeUtc(0), categoryId = categoryId, catType = 1, value = null });

            var (page1, cursor1) = await tbl.PagingByRowIdAsync(2, null);
            Assert.That(page1.Count, Is.EqualTo(2));
            Assert.That(cursor1, Is.Not.Null);

            var (page2, cursor2) = await tbl.PagingByRowIdAsync(2, cursor1);
            Assert.That(page2.Count, Is.EqualTo(1));
            Assert.That(cursor2, Is.Null);

            var (all, allCursor) = await tbl.PagingByRowIdAsync(100, null);
            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(allCursor, Is.Null);
        }
    }
}
