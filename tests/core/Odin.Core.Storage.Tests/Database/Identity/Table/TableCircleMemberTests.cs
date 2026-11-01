using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableCircleMemberTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var cl = new List<CircleMemberRecord> { new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 } };
            await tblCircleMember.UpsertCircleMembersAsync(cl);

            var r = await tblCircleMember.GetCircleMembersAsync(c1);

            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // A member's sealed copy is written by its own update, and no membership write changes or clears it.
        public async Task SealedShareKeyIsKeptByMembershipWrites(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var key = await CircleWithKeyAsync(scope, c1);
            await tblCircleMember.UpsertCircleMembersAsync([new CircleMemberRecord { circleId = c1, memberId = m1, data = [1] }]);

            var copy = new byte[] { 2, 5, 5 };
            Assert.That(await tblCircleMember.SetSealedShareKeyAsync(c1, m1, copy, key), Is.EqualTo(1));
            Assert.That(await tblCircleMember.SetSealedShareKeyAsync(c1, m2, copy, key), Is.EqualTo(0), "a non-member got a copy");

            // Both upsert paths refresh the data and keep the copy, even when the record names another
            await tblCircleMember.UpsertAsync(new CircleMemberRecord { circleId = c1, memberId = m1, data = [2] });
            await tblCircleMember.UpsertCircleMembersAsync(
                [new CircleMemberRecord { circleId = c1, memberId = m1, data = [3], sealedShareKey = [2, 8] }]);

            var stored = await tblCircleMember.GetAsync(c1, m1);
            Assert.That((stored.data, stored.sealedShareKey), Is.EqualTo((new byte[] { 3 }, copy)));

            // Re-sealing replaces it
            var resealed = new byte[] { 2, 6, 6 };
            await tblCircleMember.SetSealedShareKeyAsync(c1, m1, resealed, key);
            Assert.That((await tblCircleMember.GetAsync(c1, m1)).sealedShareKey, Is.EqualTo(resealed));

            // Leaving takes the copy; coming back starts without one
            await tblCircleMember.RemoveCircleMembersAsync(c1, [m1]);
            await tblCircleMember.UpsertAsync(new CircleMemberRecord { circleId = c1, memberId = m1, data = [4] });
            Assert.That((await tblCircleMember.GetAsync(c1, m1)).sealedShareKey, Is.Null);

            Assert.ThrowsAsync<OdinDatabaseValidationException>(() => tblCircleMember.SetSealedShareKeyAsync(c1, m1, [], key));
            Assert.ThrowsAsync<OdinDatabaseValidationException>(() => tblCircleMember.SetSealedShareKeyAsync(c1, m1, new byte[2049], key));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // A copy is stored only while the circle holds the key it was sealed from: not after a reset, not after another key
        public async Task ACopyOfAKeyTheCircleNoLongerHoldsIsRefused(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var key = await CircleWithKeyAsync(scope, c1);
            await tblCircleMember.UpsertAsync(new CircleMemberRecord { circleId = c1, memberId = m1, data = [1] });

            Assert.That(await tblCircleMember.SetSealedShareKeyAsync(c1, m1, [2, 1], [2, 9, 9]), Is.EqualTo(0), "a copy of another key");

            Assert.That(await tblCircle.ClearShareKeyAsync(c1), Is.EqualTo(1));
            Assert.That(await tblCircleMember.SetSealedShareKeyAsync(c1, m1, [2, 1], key), Is.EqualTo(0), "a copy of a reset key");
            Assert.That((await tblCircleMember.GetAsync(c1, m1)).sealedShareKey, Is.Null);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // The developer's reset clears the key and every copy, and nothing else; the cached reads see it at once
        public async Task ClearingTheKeysTouchesNothingElseAndIsSeenByTheNextRead(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var members = scope.Resolve<TableCircleMemberCached>();
            var circles = scope.Resolve<TableCircleCached>();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var key = await CircleWithKeyAsync(scope, c1);
            var otherKey = await CircleWithKeyAsync(scope, c2);
            await members.UpsertAsync(new CircleMemberRecord { circleId = c1, memberId = m1, data = [1] });
            await members.UpsertAsync(new CircleMemberRecord { circleId = c2, memberId = m1, data = [7] });
            await members.SetSealedShareKeyAsync(c1, m1, [2, 1], key);
            await members.SetSealedShareKeyAsync(c2, m1, [2, 2], otherKey);

            // Read once so every cached list holds the copies
            Assert.That((await members.GetAsync(c1, m1))!.sealedShareKey, Is.EqualTo(new byte[] { 2, 1 }));
            Assert.That((await members.GetCircleMembersAsync(c1))[0].sealedShareKey, Is.EqualTo(new byte[] { 2, 1 }));
            Assert.That((await members.GetMemberCirclesAndDataAsync(m1)).Count, Is.EqualTo(2));

            Assert.That(await members.ClearSealedShareKeysAsync(c1), Is.EqualTo(1));
            Assert.That(await circles.ClearShareKeyAsync(c1), Is.EqualTo(1));

            var row = await members.GetAsync(c1, m1);
            Assert.That((row!.data, row.sealedShareKey), Is.EqualTo((new byte[] { 1 }, (byte[])null)));
            Assert.That((await members.GetCircleMembersAsync(c1))[0].sealedShareKey, Is.Null);
            var byMember = await members.GetMemberCirclesAndDataAsync(m1);
            Assert.That(byMember.Find(r => r.circleId == c1)!.sealedShareKey, Is.Null);
            Assert.That(byMember.Find(r => r.circleId == c2)!.sealedShareKey, Is.EqualTo(new byte[] { 2, 2 }), "another circle's copy was cleared");

            var circle = await circles.GetAsync(c1);
            Assert.That((circle!.circleName, circle.appKeyLockedShareKey), Is.EqualTo(("circle", (byte[])null)));
            Assert.That((await circles.GetAsync(c2))!.appKeyLockedShareKey, Is.EqualTo(otherKey), "another circle's key was cleared");
        }

        /// <summary>A circle row holding a share key, as sealed copies need.</summary>
#if RUN_POSTGRES_TESTS
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        // Postgres runs a seal and a reset at the same time (SQLite has one writer). Whichever takes the circle's
        // row first, no member is left with a copy of the reset key: CircleShareKeyService.ResetShareKeyAsync's order
        public async Task AnOverlappingSealAndResetLeaveNoCopyOfTheResetKey(bool resetLocksFirst)
        {
            await RegisterServicesAsync(DatabaseType.Postgres);
            await using var setupScope = Services.BeginLifetimeScope();
            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var key = await CircleWithKeyAsync(setupScope, c1);
            await setupScope.Resolve<TableCircleMember>().UpsertAsync(new CircleMemberRecord { circleId = c1, memberId = m1, data = [1] });

            // Two scopes: two connections, as two requests have
            await using var resetScope = Services.BeginLifetimeScope();
            await using var sealScope = Services.BeginLifetimeScope();

            async Task<int> Reset()
            {
                var factory = resetScope.Resolve<ScopedIdentityConnectionFactory>();
                await using var cn = await factory.CreateScopedConnectionAsync();
                await using var tx = await cn.BeginStackedTransactionAsync();
                await resetScope.Resolve<TableCircle>().ClearShareKeyAsync(c1);
                var cleared = await resetScope.Resolve<TableCircleMember>().ClearSealedShareKeysAsync(c1);
                tx.Commit();
                return cleared;
            }

            if (resetLocksFirst)
            {
                var factory = resetScope.Resolve<ScopedIdentityConnectionFactory>();
                Task<int> seal;
                await using (var cn = await factory.CreateScopedConnectionAsync())
                await using (var tx = await cn.BeginStackedTransactionAsync())
                {
                    await resetScope.Resolve<TableCircle>().ClearShareKeyAsync(c1);
                    seal = Task.Run(() => sealScope.Resolve<TableCircleMember>().SetSealedShareKeyAsync(c1, m1, [2, 1], key));
                    await Task.Delay(500);
                    Assert.That(seal.IsCompleted, Is.False, "the seal didn't wait for the reset's lock on the circle");
                    await resetScope.Resolve<TableCircleMember>().ClearSealedShareKeysAsync(c1);
                    tx.Commit();
                }

                Assert.That(await seal.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(0), "a copy of the reset key was stored");
            }
            else
            {
                var factory = sealScope.Resolve<ScopedIdentityConnectionFactory>();
                Task<int> reset;
                await using (var cn = await factory.CreateScopedConnectionAsync())
                await using (var tx = await cn.BeginStackedTransactionAsync())
                {
                    Assert.That(await sealScope.Resolve<TableCircleMember>().SetSealedShareKeyAsync(c1, m1, [2, 1], key), Is.EqualTo(1));
                    reset = Task.Run(Reset);
                    await Task.Delay(500);
                    Assert.That(reset.IsCompleted, Is.False, "the reset didn't wait for the seal's lock on the circle");
                    tx.Commit();
                }

                Assert.That(await reset.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(1), "the reset didn't clear the copy sealed meanwhile");
            }

            await using var readScope = Services.BeginLifetimeScope();
            Assert.That((await readScope.Resolve<TableCircleMember>().GetAsync(c1, m1)).sealedShareKey, Is.Null);
            Assert.That((await readScope.Resolve<TableCircle>().GetAsync(c1)).appKeyLockedShareKey, Is.Null);
        }
#endif

        private static async Task<byte[]> CircleWithKeyAsync(ILifetimeScope scope, Guid circleId)
        {
            var key = new byte[] { 2, (byte)circleId.ToByteArray()[0], 3 };
            var circles = scope.Resolve<TableCircle>();
            await circles.InsertAsync(new CircleRecord { circleId = circleId, circleName = "circle", CircleSlug = "c" + circleId.ToString("N")[..8] });
            Assert.That(await circles.SetShareKeyAsync(circleId, key), Is.EqualTo(1));
            return key;
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CachedSealedShareKeySetIsSeenByTheNextRead(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var cached = scope.Resolve<TableCircleMemberCached>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var key = await CircleWithKeyAsync(scope, c1);
            await cached.UpsertAsync(new CircleMemberRecord { circleId = c1, memberId = m1, data = [1] });
            Assert.That((await cached.GetAsync(c1, m1))!.sealedShareKey, Is.Null);
            Assert.That((await cached.GetCircleMembersAsync(c1))[0].sealedShareKey, Is.Null);

            var copy = new byte[] { 2, 4, 4 };
            await cached.SetSealedShareKeyAsync(c1, m1, copy, key);
            Assert.That((await cached.GetAsync(c1, m1))!.sealedShareKey, Is.EqualTo(copy));
            Assert.That((await cached.GetCircleMembersAsync(c1))[0].sealedShareKey, Is.EqualTo(copy));
            Assert.That((await cached.GetMemberCirclesAndDataAsync(m1))[0].sealedShareKey, Is.EqualTo(copy));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertDuplicateTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();

            var cl = new List<CircleMemberRecord> { new CircleMemberRecord() { circleId = c1, memberId = m1, data = null } };
            await tblCircleMember.UpsertCircleMembersAsync(cl);

            bool ok = false;
            try
            {
                await tblCircleMember.UpsertCircleMembersAsync(cl);
            }
            catch
            {
                ok = true;
            }

            //Note: this now runs upsert so there should be no error
            ClassicAssert.IsFalse(ok);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertEmptyTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var cl = new List<CircleMemberRecord> { new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 } };

            bool ok = false;
            try
            {
                await tblCircleMember.UpsertCircleMembersAsync(new List<CircleMemberRecord>());
            }
            catch
            {
                ok = true;
            }
            ClassicAssert.IsTrue(ok);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertMultipleMembersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();

            var cl = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m2, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m3, data = d1 } };

            await tblCircleMember.UpsertCircleMembersAsync(cl);

            var r = await tblCircleMember.GetCircleMembersAsync(c1);

            ClassicAssert.IsTrue(r.Count == 3);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertMultipleCirclesTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m2, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m3, data = d1 } };

            await tblCircleMember.UpsertCircleMembersAsync(cl);

            var cl2 = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m3, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m4, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m5, data = d1 }
        };
            await tblCircleMember.UpsertCircleMembersAsync(cl2);

            var r = await tblCircleMember.GetCircleMembersAsync(c1);
            ClassicAssert.IsTrue(r.Count == 3);
            r = await tblCircleMember.GetCircleMembersAsync(c2);
            ClassicAssert.IsTrue(r.Count == 4);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task RemoveMembersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m2, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m3, data = d1 } };

            await tblCircleMember.UpsertCircleMembersAsync(cl);

            var cl2 = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m3, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m4, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m5, data = d1 }
        };
            await tblCircleMember.UpsertCircleMembersAsync(cl2);

            await tblCircleMember.RemoveCircleMembersAsync(c1, new List<Guid>() { m1, m2 });

            var r = await tblCircleMember.GetCircleMembersAsync(c1);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);

            await tblCircleMember.RemoveCircleMembersAsync(c2, new List<Guid>() { m3, m4 });
            r = await tblCircleMember.GetCircleMembersAsync(c2);
            ClassicAssert.IsTrue(r.Count == 2);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteMembersEmptyTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            bool ok = false;
            try
            {
                await tblCircleMember.DeleteMembersFromAllCirclesAsync(new List<Guid>() { });
            }
            catch
            {
                ok = true;
            }
            ClassicAssert.IsTrue(ok);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteMembersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberRecord() { circleId = c1, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c1, memberId = m3, data = d1 } };

            await tblCircleMember.UpsertCircleMembersAsync(cl);

            var cl2 = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m5, data = d1 }
            };
            await tblCircleMember.UpsertCircleMembersAsync(cl2);

            await tblCircleMember.DeleteMembersFromAllCirclesAsync(new List<Guid>() { m1, m2 });

            var r = await tblCircleMember.GetCircleMembersAsync(c1);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);

            r = await tblCircleMember.GetCircleMembersAsync(c2);
            ClassicAssert.IsTrue(r.Count == 3);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetMembersCirclesAndDataTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircleMember = scope.Resolve<TableCircleMember>();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();
            var d3 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
            new CircleMemberRecord() { circleId = c1, memberId = m2, data = d2 },
            new CircleMemberRecord() { circleId = c1, memberId = m3, data = d3 } };

            await tblCircleMember.UpsertCircleMembersAsync(cl);

            var cl2 = new List<CircleMemberRecord> {
            new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
            new CircleMemberRecord() { circleId = c2, memberId = m3, data = d2 },
            new CircleMemberRecord() { circleId = c2, memberId = m4, data = d3 },
            new CircleMemberRecord() { circleId = c2, memberId = m5, data = null }
        };
            await tblCircleMember.UpsertCircleMembersAsync(cl2);

            var r = await tblCircleMember.GetMemberCirclesAndDataAsync(m1);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);

            r = await tblCircleMember.GetMemberCirclesAndDataAsync(m2);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r[0].data, d1) == 0) || (ByteArrayUtil.muidcmp(r[0].data, d2) == 0));
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r[1].data, d1) == 0) || (ByteArrayUtil.muidcmp(r[1].data, d2) == 0));
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r[0].data, r[1].data) != 0));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PagingByRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableCircleMember>();

            var circleId = SequentialGuid.CreateGuid();
            var m1 = SequentialGuid.CreateGuid();
            var m2 = SequentialGuid.CreateGuid();
            var m3 = SequentialGuid.CreateGuid();

            await tbl.InsertAsync(new CircleMemberRecord() { circleId = circleId, memberId = m1, data = null });
            await tbl.InsertAsync(new CircleMemberRecord() { circleId = circleId, memberId = m2, data = null });
            await tbl.InsertAsync(new CircleMemberRecord() { circleId = circleId, memberId = m3, data = null });

            var (page1, cursor1) = await tbl.PagingByRowIdAsync(2, null);
            Assert.That(page1.Count, Is.EqualTo(2));
            Assert.That(cursor1, Is.Not.Null);

            var (page2, cursor2) = await tbl.PagingByRowIdAsync(2, cursor1);
            Assert.That(page2.Count, Is.EqualTo(1));
            Assert.That(cursor2, Is.Null);

            var (all, allCursor) = await tbl.PagingByRowIdAsync(100, null);
            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(allCursor, Is.Null);
        }
    }
}

