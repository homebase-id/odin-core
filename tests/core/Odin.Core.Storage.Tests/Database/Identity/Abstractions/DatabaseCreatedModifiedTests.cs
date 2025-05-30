using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions
{
    public class DatabaseCreatedModifiedTests : IocTestBase
    {
        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertTimersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };
            var n = await tblConnections.InsertAsync(item1);

            // Validate that INSERT has a modified and a "now" created that are equal
            ClassicAssert.IsTrue(n == 1);
            ClassicAssert.IsTrue(item1.modified == item1.created);
            ClassicAssert.IsTrue(item1.created <= UnixTimeUtc.Now());
            ClassicAssert.IsTrue(item1.created > UnixTimeUtc.Now().AddSeconds(-1));

            var copy = item1.created;
            Thread.Sleep(1000);

            try
            {
                n = await tblConnections.InsertAsync(item1);
                ClassicAssert.IsTrue(n == 0);
            }
            catch (Exception)
            {
            }
            // Validate that trying to insert it again doesn't mess up the values
            ClassicAssert.IsTrue(item1.modified == item1.created);
            ClassicAssert.IsTrue(item1.created.milliseconds == copy.milliseconds);

            // Validate that loading the record yields the same results
            var loaded = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            ClassicAssert.IsTrue(loaded.modified == loaded.created);
            ClassicAssert.IsTrue(item1.created == loaded.created);
        }

        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task QuickInsertUpdateTimersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };


            // Ensure that a quick Insert / Update cannot result in created == modified.
            // When created == modified it means it's an item that has never been modified, only created.
            //
            for (int i = 0; i < 100; i++)
            {
                item1.identity = new OdinId($"{i}.frodo.baggins.me");
                var n = await tblConnections.InsertAsync(item1);
                ClassicAssert.IsTrue(n == 1);
                n = await tblConnections.UpdateAsync(item1);
                ClassicAssert.IsTrue(n == 1);
                ClassicAssert.IsTrue(item1.modified != item1.created);
            }

            // Ensure that a quick Insert / Update always results in a monotonically
            // increasing modified value
            //
            var prev = item1.modified;
            for (int i = 0; i < 100; i++)
            {
                var n = await tblConnections.UpdateAsync(item1);
                ClassicAssert.IsTrue(n == 1);

                // Ensure monotonically increasing modified even if updated faster than per ms
                ClassicAssert.IsTrue(item1.modified > prev);
                prev = item1.modified;
            }
        }
        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task UpdateTimersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };
            await tblConnections.InsertAsync(item1);
            // We don't need to validate Insert, we did that above.

            var copyCreated = item1.created;
            Thread.Sleep(1000);
            await tblConnections.UpdateAsync(item1);

            // Validate that UPDATE has a value in modified and created was unchanged
            ClassicAssert.IsTrue(item1.modified != item1.created);
            ClassicAssert.IsTrue(item1.modified <= UnixTimeUtc.Now());
            ClassicAssert.IsTrue(item1.modified > UnixTimeUtc.Now().AddSeconds(-1));
            ClassicAssert.IsTrue(item1.created == copyCreated);

            // Load it and be sure the values are the same
            var loaded = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            ClassicAssert.IsTrue(loaded.modified != loaded.created);
            ClassicAssert.IsTrue(loaded.modified == item1.modified);
            ClassicAssert.IsTrue(loaded.created == item1.created);

            var copyModified = item1.modified;
            Thread.Sleep(1000);
            await tblConnections.UpdateAsync(item1);

            // Validate that UPDATE is cuurent and as expected
            ClassicAssert.IsTrue(item1.modified != item1.created);
            ClassicAssert.IsTrue(item1.modified <= UnixTimeUtc.Now());
            ClassicAssert.IsTrue(item1.modified > UnixTimeUtc.Now().AddSeconds(-1));
            ClassicAssert.IsTrue(item1.modified != copyModified);
        }

        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task UpsertTimersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };
            var n = await tblConnections.UpsertAsync(item1);

            // Validate the Upsert behaves as an INSERT for the first record
            ClassicAssert.IsTrue(n == 1);
            ClassicAssert.IsTrue(item1.modified == item1.created);
            ClassicAssert.IsTrue(item1.created <= UnixTimeUtc.Now());
            ClassicAssert.IsTrue(item1.created > UnixTimeUtc.Now().AddSeconds(-1));

            var copyCreated = item1.created;
            Thread.Sleep(1000);

            await tblConnections.UpsertAsync(item1);
            // Validate the Upsert behaves as an UPDATE for the next calls
            ClassicAssert.IsTrue(item1.modified != item1.created);
            ClassicAssert.IsTrue(item1.modified <= UnixTimeUtc.Now());
            ClassicAssert.IsTrue(item1.modified > UnixTimeUtc.Now().AddSeconds(-1));
            ClassicAssert.IsTrue(item1.created == copyCreated);

            var loaded = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            // Validate that it loads the same values
            ClassicAssert.IsTrue(loaded.modified != loaded.created);
            ClassicAssert.IsTrue(loaded.modified == item1.modified);
            ClassicAssert.IsTrue(loaded.created == item1.created);
        }
    }
}
