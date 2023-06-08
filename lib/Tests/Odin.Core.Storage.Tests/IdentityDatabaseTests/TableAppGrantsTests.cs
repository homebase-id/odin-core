using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace IdentityDatabaseTests
{
    public class TableAppGrantsTest
    {
        [Test]
        public void InsertTest()
        {   
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            var i = db.tblAppGrants.Insert(new AppGrantsRecord() { appId = c1, circleId = c2, data = d1, odinHashId = c3 });

            Debug.Assert(i == 1);
        }

    }
}