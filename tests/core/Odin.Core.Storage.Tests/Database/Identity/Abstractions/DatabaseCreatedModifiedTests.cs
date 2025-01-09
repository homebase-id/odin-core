using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
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

            // Validate that INSERT has a NULL modified and a "now" created
            Debug.Assert(n == 1);
            Debug.Assert(item1.modified == null);
            Debug.Assert(item1.created.ToUnixTimeUtc() <= UnixTimeUtc.Now());
            Debug.Assert(item1.created.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));

            var copy = item1.created;
            Thread.Sleep(1000);

            try
            {
                n = await tblConnections.InsertAsync(item1);
                Debug.Assert(n == 0);
            }
            catch (Exception)
            {
            }
            // Validate that trying to insert it again doesn't mess up the values
            Debug.Assert(item1.modified == null);
            Debug.Assert(item1.created.uniqueTime == copy.uniqueTime);

            // Validate that loading the record yields the same results
            var loaded = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(loaded.modified == null);
            Assert.IsTrue(item1.created.uniqueTime == loaded.created.uniqueTime);
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
            Assert.IsTrue(item1.modified != null);
            Assert.IsTrue(item1.modified?.ToUnixTimeUtc() <= UnixTimeUtc.Now());
            Assert.IsTrue(item1.modified?.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));
            Assert.IsTrue(item1.created.uniqueTime == copyCreated.uniqueTime);

            // Load it and be sure the values are the same
            var loaded = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(loaded.modified != null);
            Assert.IsTrue(loaded.modified?.uniqueTime == item1.modified?.uniqueTime);
            Assert.IsTrue(loaded.created.uniqueTime == item1.created.uniqueTime);


            var copyModified = item1.modified;
            Thread.Sleep(1000);
            await tblConnections.UpdateAsync(item1);

            // Validate that UPDATE is cuurent and as expected
            Assert.IsTrue(item1.modified != null);
            Assert.IsTrue(item1.modified?.ToUnixTimeUtc() <= UnixTimeUtc.Now());
            Assert.IsTrue(item1.modified?.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));
            Assert.IsTrue(item1.modified?.uniqueTime != copyModified?.uniqueTime);

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
            Debug.Assert(n == 1);
            Debug.Assert(item1.modified == null);
            Debug.Assert(item1.created.ToUnixTimeUtc() <= UnixTimeUtc.Now());
            Debug.Assert(item1.created.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));

            var copyCreated = item1.created;
            Thread.Sleep(1000);

            await tblConnections.UpsertAsync(item1);
            // Validate the Upsert behaves as an UPDATE for the next calls
            Assert.IsTrue(item1.modified != null);
            Assert.IsTrue(item1.modified?.ToUnixTimeUtc() <= UnixTimeUtc.Now());
            Assert.IsTrue(item1.modified?.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));
            Assert.IsTrue(item1.created.uniqueTime == copyCreated.uniqueTime);

            var loaded = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            // Validate that it loads the same values
            Assert.IsTrue(loaded.modified != null);
            Assert.IsTrue(loaded.modified?.uniqueTime == item1.modified?.uniqueTime);
            Assert.IsTrue(loaded.created.uniqueTime == item1.created.uniqueTime);

        }
    }
}
