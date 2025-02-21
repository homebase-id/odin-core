using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Table;
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

            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
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
            Debug.Assert(ok);
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

            Debug.Assert(r.Count == 3);
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
            Debug.Assert(r.Count == 3);
            r = await tblCircleMember.GetCircleMembersAsync(c2);
            Debug.Assert(r.Count == 4);

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
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);

            await tblCircleMember.RemoveCircleMembersAsync(c2, new List<Guid>() { m3, m4 });
            r = await tblCircleMember.GetCircleMembersAsync(c2);
            Debug.Assert(r.Count == 2);
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
            Debug.Assert(ok);
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
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);

            r = await tblCircleMember.GetCircleMembersAsync(c2);
            Debug.Assert(r.Count == 3);
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
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);

            r = await tblCircleMember.GetMemberCirclesAndDataAsync(m2);
            Debug.Assert(r.Count == 2);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].data, d1) == 0) || (ByteArrayUtil.muidcmp(r[0].data, d2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1].data, d1) == 0) || (ByteArrayUtil.muidcmp(r[1].data, d2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].data, r[1].data) != 0));
        }
    }
}

