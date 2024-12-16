using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableAppGrantsTest : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task InsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblAppGrants = scope.Resolve<TableAppGrants>();

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            var i = await tblAppGrants.InsertAsync(new AppGrantsRecord() { appId = c1, circleId = c2, data = d1, odinHashId = c3 });
            Debug.Assert(i == 1);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task TryInsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblAppGrants = scope.Resolve<TableAppGrants>();
            var identityKey = scope.Resolve<IdentityKey>();

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            var i = await tblAppGrants.TryInsertAsync(new AppGrantsRecord() { identityId = identityKey, appId = c1, circleId = c2, data = d1, odinHashId = c3 });
            Debug.Assert(i == 1);

            i = await tblAppGrants.TryInsertAsync(new AppGrantsRecord() { identityId = identityKey, appId = c1, circleId = c2, data = d1, odinHashId = c3 });
            Debug.Assert(i == 0);
        }
    }
}
