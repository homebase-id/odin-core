using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests
{
    /// <summary>
    /// Testing that the cache enabled (e.g. on the Connections table) behaves as expected.
    /// </summary>
    public class DatabaseCacheTests : IocTestBase
    {
        private bool EqualRecords(ConnectionsRecord r1, ConnectionsRecord r2)
        {
            return r1.identity.DomainName == r2.identity.DomainName && r1.displayName == r2.displayName &&
                   r1.status == r2.status && r1.accessIsRevoked == r2.accessIsRevoked &&
                   ByteArrayUtil.EquiByteArrayCompare(r1.data, r2.data);
        }

        // Test the Get() cache handling of non-existing items
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetNonExistingRowCacheTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Get a non-existing row, it'll cause inserting of a new cache null entry
            var r1 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 1);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Get a non-existing row, but now it's in the cache. We get +1 for get and +1 for hits
            var r2 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 2);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 1);

            // Get a non-existing row, that's not in the cache, just to be sure it's different
            var r3 = await tblConnections.GetAsync(new OdinId("sam.gamgee.me"));
            Assert.IsTrue(cache.GetCacheGets() == 3);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 1);
        }

        // Test the Get() cache handling of Inserting
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetExistingRowInsertCacheTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Insert a new item
            var n = await tblConnections.InsertAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Get the inserted item
            var r1 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 1);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 1);
            Assert.IsTrue(EqualRecords(item1, r1));

            // Encore
            var r2 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 2);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 2);
            Assert.IsTrue(EqualRecords(item1, r2));

        }


        // Test the Get() cache handling of Upserting
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetExistingRowUpsertCacheTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Upsert a new item
            var n = await tblConnections.UpsertAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Get the upserted item
            var r1 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 1);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 1);
            Assert.IsTrue(EqualRecords(item1, r1));

            // Encore
            var r2 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 2);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 2);
            Assert.IsTrue(EqualRecords(item1, r2));

            item1.status = 7;
            // Upsert the updated item
            n = await tblConnections.UpsertAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 2);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 2);

            // Get the upserted item, one get one hit
            var r3 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 3);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 3);
            Assert.IsTrue(EqualRecords(item1, r3));

            // Get the upserted item, one get one hit
            var r4 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 4);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 4);
            Assert.IsTrue(EqualRecords(item1, r3));

        }


        // Test the Get() cache handling of Update
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetExistingRowUpdateCacheTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Insert a new item
            var n = await tblConnections.InsertAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Update the item
            item1.status = 7;
            n = await tblConnections.UpdateAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Get the updated item, one get one hit
            var r1 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 1);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 1);
            Assert.IsTrue(EqualRecords(item1, r1));

            // Get the updated item, one get one hit
            var r2 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 2);
            Assert.IsTrue(cache.GetCacheSets() == 2);
            Assert.IsTrue(cache.GetCacheHits() == 2);
            Assert.IsTrue(EqualRecords(item1, r2));

        }


        // Test the Get() cache handling of Update
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task Delete1CacheTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);
            Assert.IsTrue(cache.GetCacheRemove() == 0);

            // Delete a non-existing item
            var n = await tblConnections.DeleteAsync(item1.identity);
            Assert.IsTrue(n == 0);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);
            Assert.IsTrue(cache.GetCacheRemove() == 0);

        }

        // Test the Get() cache handling of Update
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task Delete2CacheTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Insert a new item
            var n = await tblConnections.InsertAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);
            Assert.IsTrue(cache.GetCacheRemove() == 0);

            // Delete the item
            n = await tblConnections.DeleteAsync(item1.identity);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);
            Assert.IsTrue(cache.GetCacheRemove() == 1);

            // Encore
            n = await tblConnections.DeleteAsync(item1.identity);
            Assert.IsTrue(n == 0);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);
            Assert.IsTrue(cache.GetCacheRemove() == 1);

        }


        // Simulates data was in the DB before by clearing the cache
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetExistingRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();
            var cache = scope.Resolve<CacheHelper>();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            };

            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            // Insert a new item
            var n = await tblConnections.InsertAsync(item1);
            Assert.IsTrue(n == 1);
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);

            cache.ClearCache();
            Assert.IsTrue(cache.GetCacheGets() == 0);
            Assert.IsTrue(cache.GetCacheSets() == 0);
            Assert.IsTrue(cache.GetCacheHits() == 0);


            // The cache is now empty, the item is in the database, let's fetch it
            var r1 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 1);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 0);
            Assert.IsTrue(EqualRecords(item1, r1));

            // Encore
            var r2 = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Assert.IsTrue(cache.GetCacheGets() == 2);
            Assert.IsTrue(cache.GetCacheSets() == 1);
            Assert.IsTrue(cache.GetCacheHits() == 1);
            Assert.IsTrue(EqualRecords(item1, r2));

        }

    }
}
