using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableNonceTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task InsertInvalidExpirationThrowsTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var table = scope.Resolve<TableNonce>();

            var now = UnixTimeUtc.Now();

            // Expiration in the past
            var rPast = new NonceRecord { id = Guid.NewGuid(), data = "hello world", expiration = now.AddSeconds(-1) };
            ClassicAssert.ThrowsAsync<ArgumentException>(async () => await table.InsertAsync(rPast));

            // Expiration exactly now
            var rNow = new NonceRecord { id = Guid.NewGuid(), data = "hello world", expiration = now };
            ClassicAssert.ThrowsAsync<ArgumentException>(async () => await table.InsertAsync(rNow));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task InsertAndVerifyValidNonceTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var table = scope.Resolve<TableNonce>();

            var r = new NonceRecord { id = Guid.NewGuid(), data = "hello world", expiration = UnixTimeUtc.Now().AddSeconds(60) };

            var rows = await table.InsertAsync(r);
            ClassicAssert.AreEqual(1, rows);

            var exists = await table.VerifyAsync(r.id);
            ClassicAssert.True(exists);

            // Non-existing nonce
            var fakeId = Guid.NewGuid();
            var nonExists = await table.VerifyAsync(fakeId);
            ClassicAssert.False(nonExists);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task InsertAndPopValidNonceTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var table = scope.Resolve<TableNonce>();

            var r = new NonceRecord { id = Guid.NewGuid(), data = "hello world", expiration = UnixTimeUtc.Now().AddSeconds(60) };

            var rows = await table.InsertAsync(r);
            ClassicAssert.AreEqual(1, rows);

            var popped = await table.PopAsync(r.id);
            ClassicAssert.NotNull(popped);
            ClassicAssert.AreEqual(r.id, popped.id);
            ClassicAssert.AreEqual(r.expiration, popped.expiration);
            ClassicAssert.AreEqual(r.data, popped.data);

            // After pop, should not exist
            var existsAfterPop = await table.VerifyAsync(r.id);
            ClassicAssert.False(existsAfterPop);

            var poppedAgain = await table.PopAsync(r.id);
            ClassicAssert.Null(poppedAgain);

            // Pop non-existing
            var fakeId = Guid.NewGuid();
            var fakePop = await table.PopAsync(fakeId);
            ClassicAssert.Null(fakePop);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task VerifyAndPopExpiredNonceTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var table = scope.Resolve<TableNonce>();

            var r = new NonceRecord {id = Guid.NewGuid(), data = "hello world", expiration = UnixTimeUtc.Now().AddSeconds(2) }; // Expires in 2 seconds

            var rows = await table.InsertAsync(r);
            ClassicAssert.AreEqual(1, rows);

            var existsInitially = await table.VerifyAsync(r.id);
            ClassicAssert.True(existsInitially);

            await Task.Delay(3000); // Wait for expiration

            // Verify should return false and clean up
            var existsExpired = await table.VerifyAsync(r.id);
            ClassicAssert.False(existsExpired);

            // Pop after cleanup should be null
            var poppedAfter = await table.PopAsync(r.id);
            ClassicAssert.Null(poppedAfter);

            // Separate test for pop on expired without prior verify
            var r2 = new NonceRecord { id = Guid.NewGuid(), data = "hello world", expiration = UnixTimeUtc.Now().AddSeconds(2) };

            rows = await table.InsertAsync(r2);
            ClassicAssert.AreEqual(1, rows);

            await Task.Delay(3000);

            var poppedExpired = await table.PopAsync(r2.id);
            ClassicAssert.Null(poppedExpired);

            // Should be cleaned up (deleted)
            var existsAfterPop = await table.VerifyAsync(r2.id);
            ClassicAssert.False(existsAfterPop);
        }
    }
}
