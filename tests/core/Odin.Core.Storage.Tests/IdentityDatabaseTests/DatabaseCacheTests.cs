using System;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    /// <summary>
    /// Testing that the cache enabled (e.g. on the Connections table) behaves as expected.
    /// </summary>
    public class DatabaseCacheTests
    {
        private bool EqualRecords(ConnectionsRecord r1, ConnectionsRecord r2)
        {
            return r1.identity.DomainName == r2.identity.DomainName && r1.displayName == r2.displayName &&
                   r1.status == r2.status && r1.accessIsRevoked == r2.accessIsRevoked &&
                   ByteArrayUtil.EquiByteArrayCompare(r1.data, r2.data);
        }

        // Test the Get() cache handling of non-existing items
        [Test]
        public void GetNonExistingRowCacheTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Get a non-existing row, it'll cause inserting of a new cache null entry
                var r1 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 1);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Get a non-existing row, but now it's in the cache. We get +1 for get and +1 for hits
                var r2 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 2);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 1);

                // Get a non-existing row, that's not in the cache, just to be sure it's different
                var r3 = db.tblConnections.Get(new OdinId("sam.gamgee.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 3);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 1);
            }
        }

        // Test the Get() cache handling of Inserting
        [Test]
        public void GetExistingRowInsertCacheTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Insert a new item
                var n = db.tblConnections.Insert(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Get the inserted item
                var r1 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 1);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 1);
                Assert.IsTrue(EqualRecords(item1, r1));

                // Encore
                var r2 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 2);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 2);
                Assert.IsTrue(EqualRecords(item1, r2));
            }
        }


        // Test the Get() cache handling of Upserting
        [Test]
        public void GetExistingRowUpsertCacheTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Upsert a new item
                var n = db.tblConnections.Upsert(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Get the upserted item
                var r1 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 1);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 1);
                Assert.IsTrue(EqualRecords(item1, r1));

                // Encore
                var r2 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 2);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 2);
                Assert.IsTrue(EqualRecords(item1, r2));

                item1.status = 7;
                // Upsert the updated item
                n = db.tblConnections.Upsert(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 2);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 2);

                // Get the upserted item, one get one hit
                var r3 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 3);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 3);
                Assert.IsTrue(EqualRecords(item1, r3));

                // Get the upserted item, one get one hit
                var r4 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 4);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 4);
                Assert.IsTrue(EqualRecords(item1, r3));
            }
        }


        // Test the Get() cache handling of Update
        [Test]
        public void GetExistingRowUpdateCacheTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Insert a new item
                var n = db.tblConnections.Insert(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Update the item
                item1.status = 7;
                n = db.tblConnections.Update(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Get the updated item, one get one hit
                var r1 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 1);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 1);
                Assert.IsTrue(EqualRecords(item1, r1));

                // Get the updated item, one get one hit
                var r2 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 2);
                Assert.IsTrue(db._cache.GetCacheSets() == 2);
                Assert.IsTrue(db._cache.GetCacheHits() == 2);
                Assert.IsTrue(EqualRecords(item1, r2));
            }
        }


        // Test the Get() cache handling of Update
        [Test]
        public void Delete1CacheTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests007");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);
                Assert.IsTrue(db._cache.GetCacheRemove() == 0);

                // Delete a non-existing item
                var n = db.tblConnections.Delete(item1.identity);
                Assert.IsTrue(n == 0);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);
                Assert.IsTrue(db._cache.GetCacheRemove() == 0);
            }
        }

        // Test the Get() cache handling of Update
        [Test]
        public void Delete2CacheTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Insert a new item
                var n = db.tblConnections.Insert(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);
                Assert.IsTrue(db._cache.GetCacheRemove() == 0);

                // Delete the item
                n = db.tblConnections.Delete(item1.identity);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);
                Assert.IsTrue(db._cache.GetCacheRemove() == 1);

                // Encore
                n = db.tblConnections.Delete(item1.identity);
                Assert.IsTrue(n == 0);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);
                Assert.IsTrue(db._cache.GetCacheRemove() == 1);
            }
        }


        // Simulates data was in the DB before by clearing the cache
        [Test]
        public void GetExistingRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCacheTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = Guid.NewGuid().ToByteArray()
                };

                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                // Insert a new item
                var n = db.tblConnections.Insert(item1);
                Assert.IsTrue(n == 1);
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);

                db._cache.ClearCache();
                Assert.IsTrue(db._cache.GetCacheGets() == 0);
                Assert.IsTrue(db._cache.GetCacheSets() == 0);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);


                // The cache is now empty, the item is in the database, let's fetch it
                var r1 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 1);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 0);
                Assert.IsTrue(EqualRecords(item1, r1));

                // Encore
                var r2 = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(db._cache.GetCacheGets() == 2);
                Assert.IsTrue(db._cache.GetCacheSets() == 1);
                Assert.IsTrue(db._cache.GetCacheHits() == 1);
                Assert.IsTrue(EqualRecords(item1, r2));
            }
        }

    }
}