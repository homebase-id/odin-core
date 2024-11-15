using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableAppGrantsTest
    {
        [Test]
        public async Task InsertTest()
        {   
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppGrantTests001");

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i = await db.tblAppGrants.InsertAsync(new AppGrantsRecord() { appId = c1, circleId = c2, data = d1, odinHashId = c3 });
                Debug.Assert(i == 1);
            }
        }

        [Test]
        public async Task TryInsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppGrantTests002");

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i = await db.tblAppGrants.TryInsertAsync(myc, new AppGrantsRecord() { identityId = db._identityId, appId = c1, circleId = c2, data = d1, odinHashId = c3 });
                Debug.Assert(i == 1);

                i = await db.tblAppGrants.TryInsertAsync(myc, new AppGrantsRecord() { identityId = db._identityId, appId = c1, circleId = c2, data = d1, odinHashId = c3 });
                Debug.Assert(i == 0);
            }
        }
    }
}