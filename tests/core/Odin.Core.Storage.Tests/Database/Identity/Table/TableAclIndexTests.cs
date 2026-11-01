using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{

    public class TableAclIndexTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert and read a row
        public async Task InsertRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.Empty, "before the write");

            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, a1);

            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.EqualTo(a1), "after the write");

            // A file's ACL grants read; nothing narrower is written until Selective Share.
            var (rows, _) = await tblDriveAclIndex.PagingByRowIdAsync(10, null);
            Assert.That(rows.Select(r => (r.aclMemberId, r.permissions, r.expiresAt)),
                Is.EqualTo(new[] { (a1[0], 1, (UnixTimeUtc?)null) }));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // The generated insert sends every column, so the column's DEFAULT 1 reaches a row only through the
        // record's own default: a record nobody filled in still means read, never "nothing allowed"
        public async Task ARecordLeftUnsetMeansRead(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var member = Guid.NewGuid();
            await tblDriveAclIndex.InsertAsync(new DriveAclIndexRecord { driveId = driveId, fileId = fileId, aclMemberId = member });

            var row = await tblDriveAclIndex.GetAsync(driveId, fileId, member);
            Assert.That(row?.permissions, Is.EqualTo(1), $"a row read back as {row?.permissions}");
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Selective Share: the row is the share, so it holds the file key locked with the circle key,
        // and keeps its own created and modified apart from the file's
        public async Task ARowHoldsItsLockedFileKeyAndItsOwnTimes(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var circleId = Guid.NewGuid();
            var lockedFileKey = Enumerable.Range(0, 45).Select(i => (byte)i).ToArray();
            var before = UnixTimeUtc.Now();
            await tblDriveAclIndex.InsertAsync(new DriveAclIndexRecord
                { driveId = driveId, fileId = fileId, aclMemberId = circleId, lockedFileKey = lockedFileKey });

            var row = await tblDriveAclIndex.GetAsync(driveId, fileId, circleId);
            Assert.That(row?.lockedFileKey, Is.EqualTo(lockedFileKey));
            Assert.That(row.modified, Is.EqualTo(row.created), "a new share was modified when it was created");
            // The database's clock, not .NET's: SQLite's julianday can land a millisecond either side
            Assert.That(row.created.milliseconds, Is.InRange(before.milliseconds - 1000, UnixTimeUtc.Now().milliseconds + 1000),
                "created is the insert's time");
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert and read two aclmembers
        public async Task InsertDoubleRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(Guid.NewGuid());

            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, a1);

            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.EquivalentTo(a1));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // A member listed twice in a file's ACL gets one row
        public async Task InsertDuplicateAclMemberTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var member = Guid.NewGuid();

            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, [member, member]);

            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.EqualTo(new[] { member }));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert the same aclmember on two different keys
        public async Task InsertDoubleAclMemberTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, a1);
            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k2, a1);

            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.EqualTo(a1), "first file");
            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k2), Is.EqualTo(a1), "second file");
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        // Test we cannot insert the same key twice
        public async Task InsertDoubleKeyTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            // The table holds one row per file and member: a second insert of the same row is refused
            await tblDriveAclIndex.InsertAsync(new DriveAclIndexRecord { driveId = driveId, fileId = k1, aclMemberId = a1[0] });
            var ex = Assert.ThrowsAsync<OdinDatabaseException>(() =>
                tblDriveAclIndex.InsertAsync(new DriveAclIndexRecord { driveId = driveId, fileId = k1, aclMemberId = a1[0] }));
            Assert.That(ex!.IsUniqueConstraintViolation, Is.True, ex.ToString());

            // Writing the same ACL again keeps the one row
            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, a1);
            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.EqualTo(a1));
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            var v1 = Guid.NewGuid();
            var v2 = Guid.NewGuid();

            a1.Add(v1);
            a1.Add(v2);

            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, a1);
            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k2, a1);

            // Take all aclmembers off the first key
            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k1, []);

            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k1), Is.Empty, "the first key's members");

            // Take v2 off the second key, and make sure we have the other one
            await tblDriveAclIndex.ReconcileRowsAsync(driveId, k2, [v1]);
            Assert.That(await tblDriveAclIndex.GetAsync(driveId, k2), Is.EqualTo(new[] { v1 }), "the second key's members");
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
            var tbl = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();
            var f1 = Guid.NewGuid();
            var f2 = Guid.NewGuid();
            var f3 = Guid.NewGuid();
            var aclMember = Guid.NewGuid();

            await tbl.ReconcileRowsAsync(driveId, f1, new List<Guid> { aclMember });
            await tbl.ReconcileRowsAsync(driveId, f2, new List<Guid> { aclMember });
            await tbl.ReconcileRowsAsync(driveId, f3, new List<Guid> { aclMember });

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

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // A header write reconciles the file's ACL rows: a member that stays keeps its row as it was, locked file key
        // and times included; a member taken off loses its row; a new one gets a row; a member listed twice, one
        public async Task AHeaderWriteKeepsTheRowsOfMembersThatStay(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tbl = scope.Resolve<TableDriveAclIndex>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var stays = Guid.NewGuid();
            var leaves = Guid.NewGuid();
            var joins = Guid.NewGuid();
            var otherFileId = Guid.NewGuid();

            var mainIndex = scope.Resolve<TableDriveMainIndex>();
            await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, fileId, null, 1, 1, "frodo.baggins.me", null, null, 0,
                UnixTimeUtc.Now(), 1, [leaves], null, 1);
            await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, otherFileId, null, 1, 1, "frodo.baggins.me", null, null, 0,
                UnixTimeUtc.Now(), 1, [leaves], null, 1);

            var lockedFileKey = Enumerable.Range(0, 45).Select(i => (byte)i).ToArray();
            await tbl.InsertAsync(new DriveAclIndexRecord { driveId = driveId, fileId = fileId, aclMemberId = stays, lockedFileKey = lockedFileKey });

            var before = await tbl.GetAsync(driveId, fileId, stays);
            await RewriteHeaderAsync(metaIndex, mainIndex, driveId, fileId, [stays, joins, joins]);

            var after = await tbl.GetAsync(driveId, fileId, stays);
            Assert.That((after.rowId, after.created, after.modified, after.lockedFileKey),
                Is.EqualTo((before.rowId, before.created, before.modified, lockedFileKey)), "the row of a member that stays");
            Assert.That((await tbl.GetAsync(driveId, fileId)).OrderBy(m => m),
                Is.EqualTo(new[] { stays, joins }.OrderBy(m => m)), "the file's members after the write");
            Assert.That(await tbl.GetAsync(driveId, otherFileId), Is.EqualTo(new[] { leaves }), "another file's rows");

            await RewriteHeaderAsync(metaIndex, mainIndex, driveId, fileId, []);
            Assert.That(await tbl.GetAsync(driveId, fileId), Is.Empty, "an empty list leaves no rows");
        }

        // Writes the stored header again with a new ACL, as DriveQuery.SaveFileHeaderAsync does
        private static async Task RewriteHeaderAsync(MainIndexMeta metaIndex, TableDriveMainIndex mainIndex, Guid driveId,
            Guid fileId, List<Guid> acl)
        {
            var record = await mainIndex.GetAsync(driveId, fileId);
            Assert.That(await metaIndex.BaseUpsertEntryZapZapAsync(record, acl), Is.EqualTo(1), "the header write");
        }
    }
}
