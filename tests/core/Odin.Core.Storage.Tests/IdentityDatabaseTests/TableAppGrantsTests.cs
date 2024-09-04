using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableAppGrantsTest
    {
        [Test]
        public void InsertTest()
        {   
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppGrantTests001");

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i = db.tblAppGrants.Insert(new AppGrantsRecord() { appId = c1, circleId = c2, data = d1, odinHashId = c3 });
                Debug.Assert(i == 1);
            }
        }

        [Test]
        public void TryInsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppGrantTests002");

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i = db.tblAppGrants.TryInsert(myc, new AppGrantsRecord() { appId = c1, circleId = c2, data = d1, odinHashId = c3 });
                Debug.Assert(i == 1);

                i = db.tblAppGrants.TryInsert(myc, new AppGrantsRecord() { appId = c1, circleId = c2, data = d1, odinHashId = c3 });
                Debug.Assert(i == 0);
            }
        }
    }
}