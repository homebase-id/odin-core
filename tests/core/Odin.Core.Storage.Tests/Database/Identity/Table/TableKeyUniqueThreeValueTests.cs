using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableKeyUniqueThreeValueTests : IocTestBase
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
            var tbl = scope.Resolve<TableKeyUniqueThreeValue>();

            await tbl.InsertAsync(new KeyUniqueThreeValueRecord() { key1 = SequentialGuid.CreateGuid().ToByteArray(), key2 = SequentialGuid.CreateGuid().ToByteArray(), key3 = SequentialGuid.CreateGuid().ToByteArray(), data = null });
            await tbl.InsertAsync(new KeyUniqueThreeValueRecord() { key1 = SequentialGuid.CreateGuid().ToByteArray(), key2 = SequentialGuid.CreateGuid().ToByteArray(), key3 = SequentialGuid.CreateGuid().ToByteArray(), data = null });
            await tbl.InsertAsync(new KeyUniqueThreeValueRecord() { key1 = SequentialGuid.CreateGuid().ToByteArray(), key2 = SequentialGuid.CreateGuid().ToByteArray(), key3 = SequentialGuid.CreateGuid().ToByteArray(), data = null });

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
