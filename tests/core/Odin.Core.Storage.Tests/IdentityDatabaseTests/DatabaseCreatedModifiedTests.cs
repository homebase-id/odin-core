using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class DatabaseCreatedModifiedTests
    {
        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        public void InsertTimersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCreatedModifiedTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var g1 = Guid.NewGuid();

                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };
                var n = db.tblConnections.Insert(item1);

                // Validate that INSERT has a NULL modified and a "now" created
                Debug.Assert(n == 1);
                Debug.Assert(item1.modified == null);
                Debug.Assert(item1.created.ToUnixTimeUtc() <= UnixTimeUtc.Now());
                Debug.Assert(item1.created.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));

                var copy = item1.created;
                Thread.Sleep(1000);

                try
                {
                    n = db.tblConnections.Insert(item1);
                    Debug.Assert(n == 0);
                }
                catch (Exception)
                {
                }
                // Validate that trying to insert it again doesn't mess up the values
                Debug.Assert(item1.modified == null);
                Debug.Assert(item1.created.uniqueTime == copy.uniqueTime);

                // Validate that loading the record yields the same results
                var loaded = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(loaded.modified == null);
                Assert.IsTrue(item1.created.uniqueTime == loaded.created.uniqueTime);
            }
        }

        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        public void UpdateTimersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCreatedModifiedTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var g1 = Guid.NewGuid();

                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };
                db.tblConnections.Insert(item1);
                // We don't need to validate Insert, we did that above.

                var copyCreated = item1.created;
                Thread.Sleep(1000);
                db.tblConnections.Update(item1);

                // Validate that UPDATE has a value in modified and created was unchanged
                Assert.IsTrue(item1.modified != null);
                Assert.IsTrue(item1.modified?.ToUnixTimeUtc() <= UnixTimeUtc.Now());
                Assert.IsTrue(item1.modified?.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));
                Assert.IsTrue(item1.created.uniqueTime == copyCreated.uniqueTime);

                // Load it and be sure the values are the same
                var loaded = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                Assert.IsTrue(loaded.modified != null);
                Assert.IsTrue(loaded.modified?.uniqueTime == item1.modified?.uniqueTime);
                Assert.IsTrue(loaded.created.uniqueTime == item1.created.uniqueTime);


                var copyModified = item1.modified;
                Thread.Sleep(1000);
                db.tblConnections.Update(item1);

                // Validate that UPDATE is cuurent and as expected
                Assert.IsTrue(item1.modified != null);
                Assert.IsTrue(item1.modified?.ToUnixTimeUtc() <= UnixTimeUtc.Now());
                Assert.IsTrue(item1.modified?.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));
                Assert.IsTrue(item1.modified?.uniqueTime != copyModified?.uniqueTime);
            }
        }

        // Using the connections table just because it happens to have FinallyAddCreatedModified();
        [Test]
        public void UpsertTimersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCreatedModifiedTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var g1 = Guid.NewGuid();

                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };
                var n = db.tblConnections.Upsert(item1);

                // Validate the Upsert behaves as an INSERT for the first record
                Debug.Assert(n == 1);
                Debug.Assert(item1.modified == null);
                Debug.Assert(item1.created.ToUnixTimeUtc() <= UnixTimeUtc.Now());
                Debug.Assert(item1.created.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));

                var copyCreated = item1.created;
                Thread.Sleep(1000);
                
                db.tblConnections.Upsert(item1);
                // Validate the Upsert behaves as an UPDATE for the next calls
                Assert.IsTrue(item1.modified != null);
                Assert.IsTrue(item1.modified?.ToUnixTimeUtc() <= UnixTimeUtc.Now());
                Assert.IsTrue(item1.modified?.ToUnixTimeUtc() > UnixTimeUtc.Now().AddSeconds(-1));
                Assert.IsTrue(item1.created.uniqueTime == copyCreated.uniqueTime);

                var loaded = db.tblConnections.Get(new OdinId("frodo.baggins.me"));
                // Validate that it loads the same values
                Assert.IsTrue(loaded.modified != null);
                Assert.IsTrue(loaded.modified?.uniqueTime == item1.modified?.uniqueTime);
                Assert.IsTrue(loaded.created.uniqueTime == item1.created.uniqueTime);
            }
        }
    }
}