using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    public class DriveIndexDatabaseTests
    {
        IntRange allIntRange = new IntRange(start: 0, end: 1000);

        /// <summary>
        /// Scenario: Test batch and modified cursors on an empty database. 
        /// Expect null results in all cursors.
        /// Expect empty result set lists.
        /// </summary>
        [Test]
        public void CursorsEmpty01Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-01.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            QueryBatchCursor cursor = null;

            // Do twice on each to ensure nothing changes state wise

            var result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);

            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);

            UInt64 outCursor = 0;
            result = _testDatabase.QueryModified(10, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor == 0);
            Debug.Assert(result.Count == 0);

            result = _testDatabase.QueryModified(10, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor == 0);
            Debug.Assert(result.Count == 0);
        }


        /// <summary>
        /// Scenario: A newly installed chat client downloads the entire database. Gets everything in one go.
        /// The newest chat item will be the first result[0] and the oldest will be result[4]
        /// Tests only the QueryBatch().
        /// </summary>
        [Test]
        public void CursorsBatch02Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-02.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid()); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid()); // Most recent chat item

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);

            QueryBatchCursor cursor = null;

            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Check we got everything, we are done because result.Count < 100

            Debug.Assert(SequentialGuid.muidcmp(result[0], f5.ToByteArray()) == 0);
            Debug.Assert(SequentialGuid.muidcmp(result[4], f1.ToByteArray()) == 0);

            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(result[0], cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(result[4], cursor.pagingCursor) == 0);


            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);


            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }

        /// <summary>
        /// Scenario: Chat client gets everything but in three batches (3 pages out of 5 items)
        /// subsequent queries. Again, newest chat items returned in the first query, etc.
        /// </summary>
        [Test]
        public void CursorsBatch03Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-03.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f4.ToByteArray(), cursor.pagingCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f2.ToByteArray(), cursor.pagingCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f1.ToByteArray(), cursor.pagingCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }

        /// <summary>
        /// Scenario: We get the entire chat history. Then two new items are added. We check to get those.
        /// </summary>
        [Test]
        public void CursorsBatch04Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-04.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f1.ToByteArray(), cursor.pagingCursor) == 0);

            // Now there should be no more items
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Add two more items
            var f6 = new Guid(SequentialGuid.CreateGuid());
            var f7 = new Guid(SequentialGuid.CreateGuid());
            _testDatabase.AddEntry(f6, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f7, 1, 1, s1, t1, 0, 1, null, null);

            // Later we do a new query, with a NULL startFromCursor, because then we'll get the newest items first.
            // But stop at stopAtBoundaryCursor: pagingCursor
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(SequentialGuid.muidcmp(f6.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f7.ToByteArray(), cursor.nextBoundaryCursor) == 0);

            // Now there should be no more items
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Double check
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }


        [Test]
        public void CursorsBatch05Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-05.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            QueryBatchCursor cursor = null;

            // How you'd read the entire DB in chunks in a for loop
            int c = 0;
            for (int i = 1; i < 100; i++)
            {
                var result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 5);

            // Add two more items
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 1, null, null);

            // How you'd get the latest items (in chuinks) since your last update
            c = 0;
            for (int i = 1; i < 100; i++)
            {
                var result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 2);

            // Add five more items
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, s1, t1, 0, 0, null, null);

            // How you'd get the latest items (in chuinks) since your last update
            c = 0;
            for (int i = 1; i < 100; i++)
            {
                var result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 5);
        }


        /// <summary>
        /// Scenario: Test the cursor behavior when you get exactly the limit set & there is new data.
        /// </summary>
        [Test]
        public void CursorsBatch06Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-06.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f1.ToByteArray(), cursor.pagingCursor) == 0);

            // Add two more items
            var f6 = new Guid(SequentialGuid.CreateGuid());
            var f7 = new Guid(SequentialGuid.CreateGuid());
            _testDatabase.AddEntry(f6, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f7, 1, 1, s1, t1, 0, 1, null, null);

            // Now there should be no more items (recursive call in QueryBatch())
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(SequentialGuid.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f7.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(SequentialGuid.muidcmp(f6.ToByteArray(), cursor.pagingCursor) == 0);

            // Now there should be no more items (recursive call in QueryBatch())
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(SequentialGuid.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }


        /// <summary>
        /// Scenario: A newly installed chat client downloads the entire database. Gets everything in one go.
        /// Tests only the QueryBatch(). It's a new database, nothing is modified, so nothing gets back as modified.
        /// </summary>
        [Test]
        public void CursorsModified01Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-modified-01.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            UInt64 cursor = 0;
            var result = _testDatabase.QueryModified(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(cursor == 0);

            // Do a double check that even if the timestamp is "everything forever" then we still get nothing.
            result = _testDatabase.QueryModified(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(cursor == 0);
        }


        /// <summary>
        /// This tests a typical day in a cursor user's day. A good example of standard cursor usage.
        /// </summary>
        [Test]
        public void CursorsModified02Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-modified-06.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);


            UInt64 cursor = 0;
            var result = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);

            // Modify one item make sure we can get it.
            _testDatabase.TblMainIndex.TestTouch(f2);
            result = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(SequentialGuid.muidcmp(result[0], f2.ToByteArray()) == 0);
            // Debug.Assert(SequentialGuid.muidcmp(cursor, f2.ToByteArray()) == 0);

            // Make sure cursor is updated and we're at the end
            result = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
        }


        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        public void RequiredSecurityGroupBatch01Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-rsg-01.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();


            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            QueryBatchCursor cursor = null;

            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);

            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);

            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);

            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);

            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);

            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 4, end: 10));
            Debug.Assert(result.Count == 0);

            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 2));
            Debug.Assert(result.Count == 3);
        }


        [Test]
        public void RequiredSecurityGroupModified02Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-rsg-02.db", DatabaseIndexKind.Random);
            _testDatabase.CreateDatabase();

            var curstorUpdatedTimestamp = UnixTime.UnixTimeMillisecondsUnique();

            var f1 = new Guid(SequentialGuid.CreateGuid());
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = new Guid(SequentialGuid.CreateGuid());
            var f3 = new Guid(SequentialGuid.CreateGuid());
            var f4 = new Guid(SequentialGuid.CreateGuid());
            var f5 = new Guid(SequentialGuid.CreateGuid());

            _testDatabase.AddEntry(f1, 1, 1, s1, t1, 0, 0, null, null);
            _testDatabase.AddEntry(f2, 1, 1, s1, t1, 0, 1, null, null);
            _testDatabase.AddEntry(f3, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f4, 1, 1, s1, t1, 0, 2, null, null);
            _testDatabase.AddEntry(f5, 1, 1, s1, t1, 0, 3, null, null);

            UInt64 outCursor = 0;
            var result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing has been modified

            _testDatabase.TblMainIndex.TestTouch(f1);
            _testDatabase.TblMainIndex.TestTouch(f2);
            _testDatabase.TblMainIndex.TestTouch(f3);
            _testDatabase.TblMainIndex.TestTouch(f4);
            _testDatabase.TblMainIndex.TestTouch(f5);

            outCursor = 0;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Ensure everything is now "modified"

            outCursor = 0;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);

            outCursor = 0;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);

            outCursor = 0;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);

            outCursor = 0;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);

            outCursor = 0;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 2, end: 3));
            Debug.Assert(result.Count == 3);
        }

        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        public void UpdateTest()
        {
            var (testDatabase, fileId, conversationId, aclMembers, tags) = this.Init("update_entry_test.db");

            var _acllist = testDatabase.TblAclIndex.Get(new Guid(fileId[0]));
            var _taglist = testDatabase.TblTagIndex.Get(new Guid(fileId[0]));

            var acllist = new List<byte[]>();
            var taglist = new List<byte[]>();

            for (int i = 0; i < _acllist.Count; i++)
                acllist.Add(_acllist[i].ToByteArray());

            for (int i = 0; i < _taglist.Count; i++)
                taglist.Add(_taglist[i].ToByteArray());

            Debug.Assert(acllist.Count == 4);
            Debug.Assert(taglist.Count == 4);

            var acladd = new List<byte[]>();
            var tagadd = new List<byte[]>();
            acladd.Add(new Guid().ToByteArray());
            tagadd.Add(new Guid().ToByteArray());

            testDatabase.UpdateEntry(new Guid(fileId[0]), requiredSecurityGroup: 44, addAccessControlList: acladd, deleteAccessControlList: acllist, addTagIdList: tagadd, deleteTagIdList: taglist);
            var acllistres = testDatabase.TblAclIndex.Get(new Guid(fileId[0]));
            var taglistres = testDatabase.TblTagIndex.Get(new Guid(fileId[0]));

            Debug.Assert(acllistres.Count == 1);
            Debug.Assert(taglistres.Count == 1);

            Debug.Assert(SequentialGuid.muidcmp(acllistres[0].ToByteArray(), acladd[0]) == 0);
            Debug.Assert(SequentialGuid.muidcmp(taglistres[0].ToByteArray(), tagadd[0]) == 0);

            // Fix it back to where [0] was
            testDatabase.UpdateEntry(new Guid(fileId[0]), addAccessControlList: acllist, deleteAccessControlList: acladd, addTagIdList: taglist, deleteTagIdList: tagadd);
            acllistres = testDatabase.TblAclIndex.Get(new Guid(fileId[0]));
            taglistres = testDatabase.TblTagIndex.Get(new Guid(fileId[0]));
            Debug.Assert(acllistres.Count == 4);
            Debug.Assert(taglistres.Count == 4);
        }


        [Test]
        public void AddEntryTest()
        {
            var (testDatabase, fileId, conversationId, aclMembers, tags) = this.Init("add_entry_test.db");

            Stopwatch stopWatch = new Stopwatch();
            Console.WriteLine($"Test built in batch");

            stopWatch.Start();

            //
            // Test fetching in batches work, cursors, counts
            //

            // For the first query, save the boundaryCursor
            var cursorTimestamp = testDatabase.GetTimestamp();
            QueryBatchCursor cursor = null;

            var result = testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);
            Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[fileId.Length - 1]) == 0);
            Debug.Assert(SequentialGuid.muidcmp(result[399], fileId[fileId.Length - 400]) == 0);

            var md = testDatabase.TblMainIndex.Get(new Guid(fileId[0]));

            var p1 = testDatabase.TblAclIndex.Get(new Guid(fileId[0]));
            Debug.Assert(p1 != null);
            Debug.Assert(p1.Count == 4);

            var p2 = testDatabase.TblTagIndex.Get(new Guid(fileId[0]));
            Debug.Assert(p2 != null);
            Debug.Assert(p2.Count == 4);


            result = testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);

            result = testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 200); // We put 1,000 lines into the index. 400+400+200 = 1,000

            stopWatch.Stop();
            Utils.StopWatchStatus("Built in QueryBatch()", stopWatch);

            // Try to get a batch stopping at boundaryCursor. We should get none.
            result = testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // There should be no more

            UInt64 outCursor = 0;

            // Now let's be sure that there are no modified items. 0 gets everything that was ever modified
            result = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);

            testDatabase.UpdateEntry(new Guid(fileId[420]), fileType: 5, dataType: 6, senderId: conversationId[42], threadId: conversationId[42], userDate: 42, requiredSecurityGroup: 333);

            // Now check that we can find the one modified item with our cursor timestamp
            result = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[420]) == 0);

            md = testDatabase.TblMainIndex.Get(new Guid(fileId[420]));
            Debug.Assert(md.FileType == 5);
            Debug.Assert(md.DataType == 6);
            Debug.Assert(md.UserDate == 42);

            Assert.True(md.RequiredSecurityGroup == 333);

            UInt64 tmpCursor = UnixTime.UnixTimeMillisecondsUnique();
            // Now check that we can't find the one modified item with a newer cursor 
            result = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);


            // KIND : TimeSeries
            // Test that if we fetch the first record, it is the latest fileId
            //
            cursor = null;
            result = testDatabase.QueryBatch(1, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);

            if (testDatabase.GetKind() == DatabaseIndexKind.TimeSeries)
            {
                Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[fileId.Length - 1]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            result = testDatabase.QueryBatch(1, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            if (testDatabase.GetKind() == DatabaseIndexKind.TimeSeries)
            {
                Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[fileId.Length - 2]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            //
            // Test that fileType works. We know row #1 has filetype 0.
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor, filetypesAnyOf: new List<int>() { 0, 4 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Tags. We know row 0 has tag 0..3
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAnyOf: new List<byte[]>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);


            //
            // Test that we can find a row with Acls. We know row 0 has acl 0..3
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                aclAnyOf: new List<byte[]>() { aclMembers[0], aclMembers[1], aclMembers[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);


            //
            // Test that we can find a row with ALL Tags listed. One, two and three. 
            // From three on it's a repeat code.
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAllOf: new List<byte[]>() { tags[0] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAllOf: new List<byte[]>() { tags[0], tags[1] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            cursor = null;
            result = testDatabase.QueryBatch(1, ref cursor,
                tagsAllOf: new List<byte[]>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can execute a query with all main attributes set
            //
            cursor = null;
            result = testDatabase.QueryBatch(10,
                ref cursor,
                filetypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                datatypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                senderidAnyOf: new List<byte[]>() { tags[0] },
                threadidAnyOf: new List<byte[]>() { tags[0] },
                userdateSpan: new TimeRange() { Start = 7, End = 42 }, 
                requiredSecurityGroup: allIntRange);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAnyOf: new List<byte[]>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<byte[]>() { aclMembers[0], aclMembers[1], aclMembers[2] }, 
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            result = testDatabase.QueryBatch(100, ref cursor,
                tagsAllOf: new List<byte[]>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<byte[]>() { aclMembers[0], aclMembers[1], aclMembers[2] }, 
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(result.Count < 100);
        }

        private (DriveIndexDatabase, byte[][] _fileId, byte[][] _ConversationId, byte[][] _aclMembers, byte[][] _Tags) Init(string filename)
        {
            byte[][] fileId = new byte[1_000][];
            byte[][] conversationId = new byte[55][];
            byte[][] aclMembers = new byte[10][];
            byte[][] tags = new byte[20][];

            Utils.DummyTypes(fileId);
            Utils.DummyTypes(conversationId);
            Utils.DummyTypes(aclMembers);
            Utils.DummyTypes(tags);

            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\{filename}", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            Random myRnd = new Random();

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            int countMain = 0;
            int countAcl = 0;
            int countTags = 0;

            int[] seqAcl = new int[aclMembers.Length];
            for (int i = 0; i < seqAcl.Length; i++)
                seqAcl[i] = i;

            int[] seqTags = new int[tags.Length];
            for (int i = 0; i < seqTags.Length; i++)
                seqTags[i] = i;


            // The first two DB entries has 4 ACLs and 4 TAGs (needed for testing)
            var tmpacllist = new List<byte[]>();
            tmpacllist.Add(aclMembers[0]);
            tmpacllist.Add(aclMembers[1]);
            tmpacllist.Add(aclMembers[2]);
            tmpacllist.Add(aclMembers[3]);

            var tmptaglist = new List<byte[]>();
            tmptaglist.Add(tags[0]);
            tmptaglist.Add(tags[1]);
            tmptaglist.Add(tags[2]);
            tmptaglist.Add(tags[3]);

            _testDatabase.AddEntry(new Guid(fileId[0]), 0, 0, conversationId[0], null, 0, 55, tmpacllist, tmptaglist);

            // Insert a lot of random data
            for (var i = 0 + 1; i < fileId.Length; i++)
            {
                countMain++;

                tmpacllist = new List<byte[]>();

                for (int j = 0, r = myRnd.Next(0, 5); j < r; j++)
                {
                    int rn = myRnd.Next(j + 1, seqAcl.Length - 1);
                    int xt = Utils.swap(ref seqAcl[j], ref seqAcl[rn]);
                    tmpacllist.Add(aclMembers[seqAcl[j]]);
                    countAcl++;
                }

                tmptaglist = new List<byte[]>();

                for (int j = 0, r = myRnd.Next(0, 5); j < r; j++)
                {
                    int rn = myRnd.Next(j + 1, seqTags.Length - 1);
                    int xt = Utils.swap(ref seqTags[j], ref seqTags[rn]);
                    tmptaglist.Add(tags[seqTags[j]]);
                    countTags++;
                }

                _testDatabase.AddEntry(new Guid(fileId[i]), myRnd.Next(0, 5), myRnd.Next(0, 5), conversationId[myRnd.Next(0, conversationId.Length - 1)], null, 0, 55, tmpacllist, tmptaglist);
            }

            _testDatabase.Commit();

            stopWatch.Stop();
            Utils.StopWatchStatus($"Added {countMain + countAcl + countTags} rows: mainindex {countMain};  ACL {countAcl};  Tags {countTags}", stopWatch);

            return (_testDatabase, fileId, conversationId, aclMembers, tags);
        }
    }
}