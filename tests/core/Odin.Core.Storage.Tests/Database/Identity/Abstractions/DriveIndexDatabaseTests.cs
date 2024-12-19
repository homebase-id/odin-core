using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Test.Helpers.Benchmark;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions
{
    public class DriveIndexDatabaseTests : IocTestBase
    {
        IntRange allIntRange = new IntRange(start: 0, end: 1000);

        /// <summary>
        /// Scenario: Test batch and modified cursors on an empty database. 
        /// Expect null results in all cursors.
        /// Expect empty result set lists.
        /// </summary>

        /*
        [Test]
        public void FileLineTest()
        {
            IdentityDatabase _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"");
            await CreateDatabaseAsync();
            _testDatabase = null;
        }*/


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsEmpty01Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            QueryBatchCursor cursor = null;

            // Do twice on each to ensure nothing changes state wise

            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(refCursor.stopAtBoundary == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(moreRows == false);

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(refCursor.stopAtBoundary == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique inCursor = UnixTimeUtcUnique.ZeroTime, outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 10, inCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor.uniqueTime == 0);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 10, inCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor.uniqueTime == 0);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);
        }


        /// <summary>
        /// Scenario: A newly installed chat client downloads the entire database. Gets everything in one go.
        /// The newest chat item will be the first result[0] and the oldest will be result[4]
        /// Tests only the QueryBatch().
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBatch02Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            QueryBatchCursor cursor = null;

            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Check we got everything, we are done because result.Count < 100
            Debug.Assert(moreRows == false);

            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[4].fileId, f1) == 0);

            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId.ToByteArray(), refCursor.stopAtBoundary) == 0);

            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(moreRows == false);


            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);
        }

        /// <summary>
        /// Scenario: Chat client gets everything but in three batches (3 pages out of 5 items)
        /// subsequent queries. Again, newest chat items returned in the first query, etc.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBatch03Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(refCursor.stopAtBoundary == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.nextBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f4.ToByteArray(), refCursor.pagingCursor) == 0);
            Debug.Assert(moreRows == true);

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(refCursor.stopAtBoundary == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.nextBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f2.ToByteArray(), refCursor.pagingCursor) == 0);
            Debug.Assert(moreRows == true);

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);
        }

        /// <summary>
        /// Scenario: We get the entire chat history. Then two new items are added. We check to get those.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBatch04Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);


            // Now there should be no more items
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Add two more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f7, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            // Later we do a new query, with a NULL startFromCursor, because then we'll get the newest items first.
            // But stop at stopAtBoundaryCursor: pagingCursor
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Now there should be no more items
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Double check
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBatch05Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            QueryBatchCursor cursor = null;

            // How you'd read the entire DB in chunks in a for loop
            int c = 0;
            bool moreRows = false;
            List<DriveMainIndexRecord> result;
            for (int i = 1; i < 100; i++)
            {
                (result, moreRows, cursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 5);
            Debug.Assert(moreRows == false);

            // Add two more items
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 43, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 43, new UnixTimeUtc(0), 1, null, null, 1);

            // How you'd get the latest items (in chuinks) since your last update
            c = 0;
            for (int i = 1; i < 100; i++)
            {
                (result, moreRows, var refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 2);
            Debug.Assert(moreRows == false);

            // Add five more items
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 0, null, null, 1);

            // How you'd get the latest items (in chuinks) since your last update
            c = 0;
            for (int i = 1; i < 100; i++)
            {
                (result, moreRows, var refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 5);
            Debug.Assert(moreRows == false);
        }


        /// <summary>
        /// Scenario: Use a cursor, forward, check it stops at the given boundary.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBoundaryTest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(new UnixTimeUtc(100));
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid(new UnixTimeUtc(1000));
            var f3 = SequentialGuid.CreateGuid(new UnixTimeUtc(1999));
            var f4 = SequentialGuid.CreateGuid(new UnixTimeUtc(2000));
            var f5 = SequentialGuid.CreateGuid(new UnixTimeUtc(2001));

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 3, null, null, 1);

            QueryBatchCursor cursor = new QueryBatchCursor(f4.ToByteArray());
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 100, cursor, newestFirstOrder: false, fileIdSort: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(moreRows == false);

            Debug.Assert(ByteArrayUtil.muidcmp(f1, result[0].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f2, result[1].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f3, result[2].fileId) == 0);
        }



        /// <summary>
        /// Scenario: Use a userDate cursor, forward, check it stops at the given boundary.
        /// Will not include the "2000" boundary
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsUDBoundaryTest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(-1000), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1999), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2001), 3, null, null, 1);

            QueryBatchCursor cursor = new QueryBatchCursor(new UnixTimeUtc(2000), true);
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 100, cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(new UnixTimeUtc(2000) == cursor.userDateStopAtBoundary);
            Debug.Assert(moreRows == false);

            Debug.Assert(ByteArrayUtil.muidcmp(f1, result[0].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f2, result[1].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f3, result[2].fileId) == 0);
        }

        /// <summary>
        /// Scenario: Use a cursor, forward, check it stops at the given boundary.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBoundaryTest02(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(new UnixTimeUtc(200001));
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid(new UnixTimeUtc(20000));
            var f3 = SequentialGuid.CreateGuid(new UnixTimeUtc(2001));
            var f4 = SequentialGuid.CreateGuid(new UnixTimeUtc(2000));
            var f5 = SequentialGuid.CreateGuid(new UnixTimeUtc(1999));

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 3, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, 0, 2, null, null, 1);

            QueryBatchCursor cursor = new QueryBatchCursor(f4.ToByteArray());
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 100, cursor, newestFirstOrder: true, fileIdSort: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(moreRows == false);

            Debug.Assert(ByteArrayUtil.muidcmp(f1, result[0].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f2, result[1].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f3, result[2].fileId) == 0);

        }


        /// <summary>
        /// Scenario: Use a userDate cursor, forward, check it stops at the given boundary.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsUDBoundaryTest02(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(-1001), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(-1000), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(-999), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2001), 3, null, null, 1);

            QueryBatchCursor cursor = new QueryBatchCursor(new UnixTimeUtc(-1000), true);
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 100, cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(new UnixTimeUtc(-1000) == cursor.userDateStopAtBoundary);
            Debug.Assert(moreRows == false);

            Debug.Assert(ByteArrayUtil.muidcmp(f5, result[0].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f4, result[1].fileId) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f3, result[2].fileId) == 0);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TwoGetByTests(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var gtid1 = SequentialGuid.CreateGuid();
            var uid1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, null, null, 0, new UnixTimeUtc(0), 1, null, null, 1, fileState: 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, null, null, 0, new UnixTimeUtc(0), 2, null, null, 1, fileState: 2);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, gtid1, 1, 1, s1, null, null, 0, new UnixTimeUtc(0), 0, null, null, 1, fileState: 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, null, uid1, 1, new UnixTimeUtc(0), 2, null, null, 1, fileState: 2);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, null, null, 1, new UnixTimeUtc(0), 3, null, null, 1, fileState: 3);

            var r = await tblDriveMainIndex.GetByGlobalTransitIdAsync(driveId, gtid1);
            Debug.Assert(r != null);
            Debug.Assert(r.globalTransitId == gtid1);

            r = await tblDriveMainIndex.GetByUniqueIdAsync(driveId, uid1);
            Debug.Assert(r != null);
            Debug.Assert(r.uniqueId == uid1);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task FileStateTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 0, null, null, 1, fileState: 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 1, null, null, 1, fileState: 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 2, null, null, 1, fileState: 2);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 1, new UnixTimeUtc(0), 2, null, null, 1, fileState: 2);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 1, new UnixTimeUtc(0), 3, null, null, 1, fileState: 3);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange, fileStateAnyOf: new List<Int32>() { 0 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange, fileStateAnyOf: new List<Int32>() { 3 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange, fileStateAnyOf: new List<Int32>() { 1, 2 });
            Debug.Assert(result.Count == 4);
            Debug.Assert(moreRows == false);

            var r = await tblDriveMainIndex.GetAsync(driveId, f1);
            r.fileState = 42;
            await metaIndex.BaseUpdateEntryZapZapAsync(r, null, null);

            var c2 = new UnixTimeUtcUnique(0);
            (result, moreRows, var outc2) = await metaIndex.QueryModifiedAsync(driveId, 10, c2, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            r = await tblDriveMainIndex.GetAsync(driveId, f2);
            r.fileState = 43;
            await metaIndex.BaseUpdateEntryZapZapAsync(r, null, null);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, fileStateAnyOf: new List<Int32>() { 42, 43 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task ArchivalStatusTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 1, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 1, new UnixTimeUtc(0), 3, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 2, new UnixTimeUtc(0), 0, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 6);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange, archivalStatusAnyOf: new List<Int32>() { 0 });
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange, archivalStatusAnyOf: new List<Int32>() { 1 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, requiredSecurityGroup: allIntRange, archivalStatusAnyOf: new List<Int32>() { 2 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, archivalStatusAnyOf: new List<Int32>() { 0, 1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique c2 = new UnixTimeUtcUnique(0), outc2 = new UnixTimeUtcUnique(0);
            (result, moreRows, outc2) = await metaIndex.QueryModifiedAsync(driveId, 10, c2, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            var r = await tblDriveMainIndex.GetAsync(driveId, f1);
            r.archivalStatus = 7;
            await metaIndex.BaseUpdateEntryZapZapAsync(r, null, null);

            c2 = new UnixTimeUtcUnique(0);
            (result, moreRows, outc2) = await metaIndex.QueryModifiedAsync(driveId, 10, c2, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, archivalStatusAnyOf: new List<Int32>() { 0 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            r = await tblDriveMainIndex.GetAsync(driveId, f2);
            r.archivalStatus = 7;
            await metaIndex.BaseUpdateEntryZapZapAsync(r, null, null);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, cursor, archivalStatusAnyOf: new List<Int32>() { 0 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

        }


        /// <summary>
        /// Scenario: Test the cursor behavior when you get exactly the limit set & there is new data.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBatch06Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Add two more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f7, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            // Now there should be no more items (recursive call in QueryBatch())
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Now there should be no more items (recursive call in QueryBatch())
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

        }


        /// <summary>
        /// READ THIS EXAMPLE TO UNDERSTAND HOW THE AUTO CURSOR WORKS IN A REAL LIFE SCENARIO
        ///
        /// Scenario: First we get the entire chat history of five items.
        /// Then three new items are added.
        /// We check to get a page of TWO those (one is left).
        /// Then two new items are added.
        /// We check to get TWO items. We'll get only 1 because that's the leftover from the three items where we only got 2
        /// Then we check to get TWO more items, and we get the last two.
        ///
        /// In summary items are retrieved as [f5,f4,f3,f2,f1], [f8,f7], [f6], [f10,f9]
        ///
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsBatch07ExampleTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            // Add five items to the chat database
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            // Get everything from the chat database
            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[2].fileId, f3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[3].fileId, f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[4].fileId, f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(moreRows == false);

            // Now there should be no more items
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Now add three more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            var f8 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f7, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f8, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            // Now we get two of the three new items, we get the newest first f8 & f7
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f8) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f7) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), refCursor.pagingCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f8.ToByteArray(), refCursor.nextBoundaryCursor) == 0);
            Debug.Assert(moreRows == true);


            // Now add two more items
            var f9 = SequentialGuid.CreateGuid();
            var f10 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f9, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f10, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            // Now we get two more items. Internally, this will turn into two QueryBatchRaw()
            // because there is only 1 left in the previous range. A second request will get the
            // next item. Leaving us with 1 left over. The order of the items will be newest first,
            // so f10, f6. Note that you'll get a gap between {f8,f7,f6} and {f10,f9}, i.e. f9 still
            // waiting for the next query
            //
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f10) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f6) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), refCursor.pagingCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f8.ToByteArray(), refCursor.stopAtBoundary) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), refCursor.nextBoundaryCursor) == 0);
            Debug.Assert(moreRows == true);

            // Now we get two more items, only one should be left (f9)
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 2, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f9) == 0);
            Debug.Assert(moreRows == false);

            Debug.Assert(refCursor.nextBoundaryCursor == null);
            Debug.Assert(refCursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), refCursor.stopAtBoundary) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchCursorNewestHasRows01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchUserDateCursorNewestHasRows01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchCursorOldestHasRows01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchUserDateCursorOldestHasRows01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchCursorNewest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f2.ToByteArray()) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f1.ToByteArray()) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f1.ToByteArray()) == 0);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchUserDateCursorNewest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(42), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f1) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f2.ToByteArray()) == 0);
            Debug.Assert(refCursor.userDatePagingCursor.Value.milliseconds == 42);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchCursorOldest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f2.ToByteArray()) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f3.ToByteArray()) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f3.ToByteArray()) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchUserDateCursorOldest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(42), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 2, cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f1.ToByteArray()) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f3.ToByteArray()) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f3) == 0);

            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, refCursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(refCursor.pagingCursor, f3.ToByteArray()) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task QueryBatchCursorOldestNewest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            // Check we get the oldest and newest items

            QueryBatchCursor cursor = null;
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f3) == 0);

            cursor = null;
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 1, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f1) == 0);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TestQueryBatchStartPointGuid(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            // Set the start point to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor();
            cursor.CursorStartPoint(f3.ToByteArray());

            // Get all the newest items. We should get f2, f1 and no more because f3 is the start point.
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor.CursorStartPoint(f3.ToByteArray());

            // Get all the oldest items. We should get f4,f5,f6 because f3 is the start point and we're getting oldest first.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f6.ToByteArray()) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TestQueryBatchStartPointTime(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            Thread.Sleep(1);
            var t3 = UnixTimeUtc.Now();
            Thread.Sleep(1);
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            // Set the start point to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor();
            cursor.CursorStartPoint(t3, false);

            // Get all the newest items. We should get f2, f1 and no more because f3 is the start point.
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor.CursorStartPoint(t3, false);

            // Get all the oldest items. We should get f4,f5,f6 because f3 is the start point and we're getting oldest first.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f6.ToByteArray()) == 0);

        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TestQueryBatchUserDateStartPointTime(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            Thread.Sleep(1);
            var t3 = UnixTimeUtc.Now();
            Thread.Sleep(1);
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(5000), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(4000), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(3000), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 2, null, null, 1);

            // Set the start point to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor();
            cursor.CursorStartPoint(new UnixTimeUtc(4000), true);

            // Get all the newest items. We should get f2, f1 and no more because f3 is the start point.
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[2].fileId, f6) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor.CursorStartPoint(new UnixTimeUtc(4000), true);

            // Get all the oldest items. We should get f4,f5,f6 because f3 is the start point and we're getting oldest first.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f2) == 0);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TestQueryBatchStopBoundaryGuid(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            // Set the boundary item to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor(f3.ToByteArray());

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor = new QueryBatchCursor(f3.ToByteArray());

            // Get all the oldest items. We should get f1, f2 and no more because f3 is the boundary.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TestQueryBatchStopBoundaryTime(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            Thread.Sleep(1);
            var t3 = UnixTimeUtc.Now();
            Thread.Sleep(1);
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);

            // Set the boundary item to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor(t3, false);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            var (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor = new QueryBatchCursor(t3, false);

            // Get all the oldest items. We should get f1, f2 and no more because f3 is the boundary.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows, refCursor) = await metaIndex.QueryBatchAsync(driveId, 10, cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

        }

        /// <summary>
        /// Scenario: A newly installed chat client downloads the entire database. Gets everything in one go.
        /// Tests only the QueryBatch(). It's a new database, nothing is modified, so nothing gets back as modified.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsModified01Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            UnixTimeUtcUnique cursor = UnixTimeUtcUnique.ZeroTime;
            var (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(outCursor.uniqueTime == 0);
            Debug.Assert(moreRows == false);

            // Do a double check that even if the timestamp is "everything forever" then we still get nothing.
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(outCursor.uniqueTime == 0);
            Debug.Assert(moreRows == false);

        }


        /// <summary>
        /// This tests a typical day in a cursor user's day. A good example of standard cursor usage.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CursorsModified02Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);


            UnixTimeUtcUnique cursor = UnixTimeUtcUnique.ZeroTime;
            var (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 2, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // Modify one item make sure we can get it.
            await tblDriveMainIndex.TestTouchAsync(driveId, f2);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 2, outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);
            // Debug.Assert(ByteArrayUtil.muidcmp(cursor, f2.ToByteArray()) == 0);
            Debug.Assert(moreRows == false);

            // Make sure cursor is updated and we're at the end
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 2, outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

        }


        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task RequiredSecurityGroupBatch01Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            QueryBatchCursor cursor = null;

            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 4, end: 10));
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 2));
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task RequiredSecurityGroupModified02Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 1);

            UnixTimeUtcUnique inCursor = UnixTimeUtcUnique.ZeroTime;
            var (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing has been modified
            Debug.Assert(moreRows == false);

            await tblDriveMainIndex.TestTouchAsync(driveId, f1);
            await tblDriveMainIndex.TestTouchAsync(driveId, f2);
            await tblDriveMainIndex.TestTouchAsync(driveId, f3);
            await tblDriveMainIndex.TestTouchAsync(driveId, f4);
            await tblDriveMainIndex.TestTouchAsync(driveId, f5);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Ensure everything is now "modified"
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, inCursor, requiredSecurityGroup: new IntRange(start: 2, end: 3));
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task SecurityGroupAndAclBatch01Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var a1 = SequentialGuid.CreateGuid();
            var a2 = SequentialGuid.CreateGuid();
            var a3 = SequentialGuid.CreateGuid();
            var a4 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a1 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a2 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a1, a2 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a3, a4 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: null, null, 1);

            QueryBatchCursor cursor = null;

            // For any security group, we should have 5 entries
            cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            // For any security group, and an ACL, test the AND statement
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // For NO valid security group, and a valid ACL, just the valid ACLs
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // For just security Group 1 we have 2 entries
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // For security Group 1 or any of the ACLs a1 we have 3
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            // For security Group 1 or any of the ACLs a3, a4 we have 3
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a3, a4 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // For no security Group 1 getting ACLs a1we have 2
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

        }


        // XXX
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task SecurityGroupAndAclBatch02Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var a1 = SequentialGuid.CreateGuid();
            var a2 = SequentialGuid.CreateGuid();
            var a3 = SequentialGuid.CreateGuid();
            var a4 = SequentialGuid.CreateGuid();
            var a5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a1 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a2 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a1, a2 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a3, a4 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: null, null, 1);

            QueryBatchCursor cursor = null;

            // ===== TEST RSG, no circles

            // ACL: Any security group, no circles. We should have 5 entries
            cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, no circles. We should have 2 entries
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 0, no circles. We should have 0 entries
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ======== TEST any RSG with circle combinations

            // ACL: Any security group, circles a4. We should have 2 (one with a4, one with no circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Any security group, circles a2. We should have 3 (two with a2, one with no circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a2 });
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

            // ACL: Any security group, circles a1, a2. We should have 4 (two with a2, one with a1, one with no circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 4);
            Debug.Assert(moreRows == false);

            // ACL: Any security group, circles a5. We should have 1 (none with a5, one with no circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            // ======== TEST no RSG with circles

            // ACL: No security group, circles a4. We should have none
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: No security group, circles a2. We should have none
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a2 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: No security group, circles a1, a2. We should have none
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: No security group, circles a5. We should have none
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ======== Test partial RSG with circle combinations

            // ACL: One security group 2, circles a2. We should have 2 (one with a2, one with no circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2), aclAnyOf: new List<Guid>() { a2 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, circles a4. We should have 0 (none with a4, none with circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, circles a1, a2. We should have 2
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 2, circles a1, a2. We should have 2
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2), aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, circles a5. We should have 0 (none with a5, none with circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: Security group 2, circles a5. We should have 1 (none with a5, one with no circles)
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2), aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            // ========
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task SecurityGroupAndAclBatch02ModifiedTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var a1 = SequentialGuid.CreateGuid();
            var a2 = SequentialGuid.CreateGuid();
            var a3 = SequentialGuid.CreateGuid();
            var a4 = SequentialGuid.CreateGuid();
            var a5 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a1 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a2 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a1, a2 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a3, a4 }, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: null, null, 1);


            await tblDriveMainIndex.TestTouchAsync(driveId, f1);
            await tblDriveMainIndex.TestTouchAsync(driveId, f2);
            await tblDriveMainIndex.TestTouchAsync(driveId, f3);
            await tblDriveMainIndex.TestTouchAsync(driveId, f4);
            await tblDriveMainIndex.TestTouchAsync(driveId, f5);


            UnixTimeUtcUnique cursor;

            // ===== TEST RSG, no circles

            // ACL: Any security group, no circles. We should have 5 entries
            cursor = new UnixTimeUtcUnique(0);
            var (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, no circles. We should have 2 entries
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 0, no circles. We should have 0 entries
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ======== TEST any RSG with circle combinations

            // ACL: Any security group, circles a4. We should have 2 (one with a4, one with no circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Any security group, circles a2. We should have 3 (two with a2, one with no circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a2 });
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

            // ACL: Any security group, circles a1, a2. We should have 4 (two with a2, one with a1, one with no circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 4);
            Debug.Assert(moreRows == false);

            // ACL: Any security group, circles a5. We should have 1 (none with a5, one with no circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            // ======== TEST no RSG with circles

            // ACL: No security group, circles a4. We should have none
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: No security group, circles a2. We should have none
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a2 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: No security group, circles a1, a2. We should have none
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: No security group, circles a5. We should have none
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ======== Test partial RSG with circle combinations

            // ACL: One security group 2, circles a2. We should have 2 (one with a2, one with no circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2), aclAnyOf: new List<Guid>() { a2 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, circles a4. We should have 0 (none with a4, none with circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, circles a1, a2. We should have 2
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 2, circles a1, a2. We should have 2
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2), aclAnyOf: new List<Guid>() { a1, a2 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // ACL: Security group 1, circles a5. We should have 0 (none with a5, none with circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // ACL: Security group 2, circles a5. We should have 1 (none with a5, one with no circles)
            cursor = new UnixTimeUtcUnique(0);
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 400, cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2), aclAnyOf: new List<Guid>() { a5 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            // ========

        }





        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can add one and retrieve it
        public async Task GlobalTransitId01Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f1) == 0);
            var data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g1) == 0);
            Debug.Assert(moreRows == false);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can add two and retrieve them
        public async Task GlobalTransitId02Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            var f2 = SequentialGuid.CreateGuid();
            var g2 = Guid.NewGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, g2, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);
            var data = await tblDriveMainIndex.GetAsync(driveId, f2);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g2) == 0);

            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f1) == 0);
            data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g1) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test that we cannot add a duplicate
        public async Task GlobalTransitId03Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, g1, 1, 1, s1.ToString(), t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            try
            {
                var f2 = SequentialGuid.CreateGuid();
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, g1, 1, 1, s1.ToString(), t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can handle NULL
        public async Task GlobalTransitId04Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f1) == 0);
            var data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(data.globalTransitId == null);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can add one and retrieve it searching for a specific GTID guid
        public async Task GlobalTransitId05Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            cursor = null;
            // We shouldn't be able to find any like this:
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor, globalTransitIdAnyOf: new List<Guid>() { t1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // Now we should be able to find it
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor, globalTransitIdAnyOf: new List<Guid>() { t1, g1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique inCursor = UnixTimeUtcUnique.ZeroTime, outCursor = UnixTimeUtcUnique.ZeroTime;
            await tblDriveMainIndex.TestTouchAsync(driveId, f1); // Make sure we can find it
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 1, inCursor, globalTransitIdAnyOf: new List<Guid>() { t1, g1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can modify the global transit guid with both update versions
        public async Task GlobalTransitId06Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var g3 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            var data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g1) == 0);

            await metaIndex.UpdateEntryZapZapPassAlongAsync(driveId, f1, globalTransitId: g2, archivalStatus: 7);
            data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g2) == 0);

            await metaIndex.UpdateEntryZapZapPassAlongAsync(driveId, f1, globalTransitId: g3);
            data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g3) == 0);
        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can add one and retrieve it
        public async Task UniqueId01Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f1) == 0);
            var data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can add two and retrieve them
        public async Task UniqueId02Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null, 1);

            var f2 = SequentialGuid.CreateGuid();
            var u2 = Guid.NewGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, null, 1, 1, s1, t1, u2, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f2) == 0);
            var data = await tblDriveMainIndex.GetAsync(driveId, f2);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u2) == 0);

            Debug.Assert(ByteArrayUtil.muidcmp(result[1].fileId, f1) == 0);
            data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test that we cannot add a duplicate
        public async Task UniqueId03Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1.ToString(), t1, u1, 42, new UnixTimeUtc(0), 1, null, null, 1);

            try
            {
                var f2 = SequentialGuid.CreateGuid();
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, null, 1, 1, s1.ToString(), t1, u1, 42, new UnixTimeUtc(0), 1, null, null, 1);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can handle NULL
        public async Task UniqueId04Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, f1) == 0);
            var data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(data.uniqueId == null);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can add one and retrieve it searching for a specific GTID guid
        public async Task UniqueId05Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null, 1);

            QueryBatchCursor cursor = null;
            cursor = null;
            // We shouldn't be able to find any like this:
            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor, uniqueIdAnyOf: new List<Guid>() { t1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // Now we should be able to find it
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor, uniqueIdAnyOf: new List<Guid>() { t1, u1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique inCursor = UnixTimeUtcUnique.ZeroTime, outCursor = UnixTimeUtcUnique.ZeroTime;
            await tblDriveMainIndex.TestTouchAsync(driveId, f1); // Make sure we can find it
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 1, inCursor, uniqueIdAnyOf: new List<Guid>() { t1, u1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can modify the global transit guid with both update versions
        public async Task UniqueId06Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var u2 = Guid.NewGuid();
            var u3 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null, 1);

            var data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);

            await metaIndex.UpdateEntryZapZapPassAlongAsync(driveId, f1, uniqueId: u2);
            data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u2) == 0);

            await metaIndex.UpdateEntryZapZapPassAlongAsync(driveId, f1, uniqueId: u3);
            data = await tblDriveMainIndex.GetAsync(driveId, f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u3) == 0);

        }



        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task UpdateTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var (driveId, fileId, conversationId, aclMembers, tags) = await this.InitAsync(metaIndex);

            var _acllist = await tblDriveAclIndex.GetAsync(driveId, fileId[0]);
            var _taglist = await tblDriveTagIndex.GetAsync(driveId, fileId[0]);

            var acllist = new List<Guid>();
            var taglist = new List<Guid>();

            for (int i = 0; i < _acllist.Count; i++)
                acllist.Add(_acllist[i]);

            for (int i = 0; i < _taglist.Count; i++)
                taglist.Add(_taglist[i]);

            Debug.Assert(acllist.Count == 4);
            Debug.Assert(taglist.Count == 4);

            var acladd = new List<Guid>();
            var tagadd = new List<Guid>();
            acladd.Add(Guid.NewGuid());
            tagadd.Add(Guid.NewGuid());
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task AddEntryTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var tblDriveAclIndex = scope.Resolve<TableDriveAclIndex>();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var (driveId, fileId, conversationId, aclMembers, tags) = await this.InitAsync(metaIndex);

            Stopwatch stopWatch = new Stopwatch();
            Console.WriteLine($"Test built in batch");

            stopWatch.Start();

            //
            // Test fetching in batches work, cursors, counts
            //

            // For the first query, save the boundaryCursor
            // var cursorTimestamp = testDatabase.GetTimestamp();
            QueryBatchCursor cursor = null;

            var (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, fileId[fileId.Count - 1]) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[399].fileId, fileId[fileId.Count - 400]) == 0);
            Debug.Assert(moreRows == true);

            var md = await tblDriveMainIndex.GetAsync(driveId, fileId[0]);

            var p1 = await tblDriveAclIndex.GetAsync(driveId, fileId[0]);
            Debug.Assert(p1 != null);
            Debug.Assert(p1.Count == 4);

            var p2 = await tblDriveTagIndex.GetAsync(driveId, fileId[0]);
            Debug.Assert(p2 != null);
            Debug.Assert(p2.Count == 4);


            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);
            Debug.Assert(moreRows == true);

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 200); // We put 1,000 lines into the index. 400+400+200 = 1,000
            Debug.Assert(moreRows == false);

            stopWatch.Stop();
            TestBenchmark.StopWatchStatus("Built in QueryBatch(driveId, )", stopWatch);

            // Try to get a batch stopping at boundaryCursor. We should get none.
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 400, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // There should be no more
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique inCursor = UnixTimeUtcUnique.ZeroTime, outCursor = UnixTimeUtcUnique.ZeroTime;
            // Now let's be sure that there are no modified items. 0 gets everything that was ever modified
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 100, inCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            var theguid = conversationId[42];

            var r = await tblDriveMainIndex.GetAsync(driveId, fileId[420]);
            r.fileType = 5;
            r.dataType = 6;
            r.senderId = conversationId[42].ToString();
            r.groupId = theguid;
            r.userDate = new UnixTimeUtc(42);
            r.requiredSecurityGroup = 333;
            await metaIndex.BaseUpdateEntryZapZapAsync(r, null, null);
            //UpdateEntryZapZapPassAlong(myc, driveId, fileId[420], fileType: 5, dataType: 6, senderId: conversationId[42].ToByteArray(), groupId: theguid, userDate: new UnixTimeUtc(42), requiredSecurityGroup: 333);

            // Now check that we can find the one modified item with our cursor timestamp
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 100, outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, fileId[420]) == 0);
            Debug.Assert(moreRows == false);

            md = await tblDriveMainIndex.GetAsync(driveId, fileId[420]);
            Debug.Assert(md.fileType == 5);
            Debug.Assert(md.dataType == 6);
            Debug.Assert(md.userDate == new UnixTimeUtc(42));

            Assert.True(md.requiredSecurityGroup == 333);

            // UInt64 tmpCursor = UnixTime.UnixTimeMillisecondsUnique();
            // Now check that we can't find the one modified item with a newer cursor 
            (result, moreRows, outCursor) = await metaIndex.QueryModifiedAsync(driveId, 100, outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // KIND : TimeSeries
            // Test that if we fetch the first record, it is the latest fileId
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == true);

            if (true)
            {
                Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, fileId[fileId.Count - 1]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, refCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == true);
            if (true)
            {
                Debug.Assert(ByteArrayUtil.muidcmp(result[0].fileId, fileId[fileId.Count - 2]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            //
            // Test that fileType works. We know row #1 has filetype 0.
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor, filetypesAnyOf: new List<int>() { 0, 4 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(moreRows == true);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Tags. We know row 0 has tag 0..3
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor,
                tagsAnyOf: new List<Guid>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);


            //
            // Test that we can find a row with Acls. We know row 0 has acl 0..3
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor,
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == true);


            //
            // Test that we can find a row with ALL Tags listed. One, two and three. 
            // From three on it's a repeat code.
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor,
                tagsAllOf: new List<Guid>() { tags[0] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 1, cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            //
            // Test that we can execute a query with all main attributes set
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 10,
                cursor,
                filetypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                datatypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                senderidAnyOf: new List<string>() { tags[0].ToString() },
                groupIdAnyOf: new List<Guid>() { tags[0] },
                userdateSpan: new UnixTimeUtcRange(new UnixTimeUtc(7), new UnixTimeUtc(42)),
                requiredSecurityGroup: allIntRange);
            Debug.Assert(moreRows == false);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor,
                tagsAnyOf: new List<Guid>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] },
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            (result, moreRows, refCursor) = await metaIndex.QueryBatchAutoAsync(driveId, 100, cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] },
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(result.Count < 100);
            Debug.Assert(moreRows == false);

        }

        private async Task<(Guid driveId, List<Guid> _fileId, List<Guid> _ConversationId, List<Guid> _aclMembers, List<Guid> _Tags)> InitAsync(MainIndexMeta metaIndex)
        {
            var fileId = new List<Guid>();
            var conversationId = new List<Guid>();
            var aclMembers = new List<Guid>();
            var tags = new List<Guid>();

            Utils.DummyTypes(fileId, 1000);
            Utils.DummyTypes(conversationId, 1000);
            Utils.DummyTypes(aclMembers, 1000);
            Utils.DummyTypes(tags, 1000);

            var driveId = Guid.NewGuid();

            Random myRnd = new Random();

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            int countMain = 0;
            int countAcl = 0;
            int countTags = 0;

            int[] seqAcl = new int[aclMembers.Count];
            for (int i = 0; i < seqAcl.Length; i++)
                seqAcl[i] = i;

            int[] seqTags = new int[tags.Count];
            for (int i = 0; i < seqTags.Length; i++)
                seqTags[i] = i;


            // The first two DB entries has 4 ACLs and 4 TAGs (needed for testing)
            var tmpacllist = new List<Guid>();
            tmpacllist.Add(aclMembers[0]);
            tmpacllist.Add(aclMembers[1]);
            tmpacllist.Add(aclMembers[2]);
            tmpacllist.Add(aclMembers[3]);

            var tmptaglist = new List<Guid>();
            tmptaglist.Add(tags[0]);
            tmptaglist.Add(tags[1]);
            tmptaglist.Add(tags[2]);
            tmptaglist.Add(tags[3]);

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, fileId[0], Guid.NewGuid(), 0, 0, conversationId[0].ToString(), null, null, 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);

            // Insert a lot of random data
            for (var i = 0 + 1; i < fileId.Count; i++)
            {
                countMain++;

                tmpacllist = new List<Guid>();

                for (int j = 0, r = myRnd.Next(0, 5); j < r; j++)
                {
                    int rn = myRnd.Next(j + 1, seqAcl.Length - 1);
                    int xt = Utils.swap(ref seqAcl[j], ref seqAcl[rn]);
                    tmpacllist.Add(aclMembers[seqAcl[j]]);
                    countAcl++;
                }

                tmptaglist = new List<Guid>();

                for (int j = 0, r = myRnd.Next(0, 5); j < r; j++)
                {
                    int rn = myRnd.Next(j + 1, seqTags.Length - 1);
                    int xt = Utils.swap(ref seqTags[j], ref seqTags[rn]);
                    tmptaglist.Add(tags[seqTags[j]]);
                    countTags++;
                }

                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, fileId[i], Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), conversationId[myRnd.Next(0, conversationId.Count - 1)].ToString(), null, null, 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }

            stopWatch.Stop();
            TestBenchmark.StopWatchStatus($"Added {countMain + countAcl + countTags} rows: mainindex {countMain};  ACL {countAcl};  Tags {countTags}", stopWatch);

            return (driveId, fileId, conversationId, aclMembers, tags);
        }
    }
}
