using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Util;
using UnixTime = Youverse.Core.UnixTimeUtcUniqueGenerator;

namespace IndexerTests
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

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(10, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor.uniqueTime == 0);
            Debug.Assert(result.Count == 0);

            result = _testDatabase.QueryModified(10, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor.uniqueTime == 0);
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

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);

            QueryBatchCursor cursor = null;

            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Check we got everything, we are done because result.Count < 100

            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[4], f1) == 0);

            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);


            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
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

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f4.ToByteArray(), cursor.pagingCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.currentBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f2.ToByteArray(), cursor.pagingCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }

        /// <summary>
        /// Scenario: We get the entire chat history. Then two new items are added. We check to get those.
        /// </summary>
        [Test]
        public void CursorsBatch04Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-04.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Now there should be no more items
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Add two more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f7, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);

            // Later we do a new query, with a NULL startFromCursor, because then we'll get the newest items first.
            // But stop at stopAtBoundaryCursor: pagingCursor
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Now there should be no more items
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Double check
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }


        [Test]
        public void CursorsBatch05Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-05.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

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
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);

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
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);

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

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Add two more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f7, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);

            // Now there should be no more items (recursive call in QueryBatch())
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Now there should be no more items (recursive call in QueryBatch())
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.currentBoundaryCursor) == 0);
        }


        /// <summary>
        /// READ THIS EXAMPLE TO UNDERSTAND HOW THE CURSOR WORKS IN A RAL LIFE SCENARIO
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
        public void CursorsBatch07Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-cursor-07.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            // Add five items to the chat database
            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

            // Get everything from the chat database
            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[2], f3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[3], f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[4], f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);

            // Now there should be no more items
            result = _testDatabase.QueryBatch(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);

            // Now add three more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            var f8 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f7, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f8, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);

            // Now we get two of the three new items, we get the newest first f8 & f7
            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f8) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f7) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f8.ToByteArray(), cursor.nextBoundaryCursor) == 0);


            // Now add two more items
            var f9 = SequentialGuid.CreateGuid();
            var f10 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f9, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f10, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);

            // Now we get two more items. Internally, this will turn into two QueryBatchRaw()
            // because there is only 1 left in the previous range. A second request will get the
            // next item. Leaving us with 1 left over. The order of the items will be newest first,
            // so f10, f6. Note that you'll get a gap between {f8,f7,f6} and {f10,f9}, i.e. f9 still
            // waiting for the next query
            //
            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f10) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f6) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f8.ToByteArray(), cursor.currentBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), cursor.nextBoundaryCursor) == 0);


            // Now we get two more items, only one should be left (f9)
            result = _testDatabase.QueryBatch(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f9) == 0);

            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), cursor.currentBoundaryCursor) == 0);
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

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

            UnixTimeUtcUnique cursor = UnixTimeUtcUnique.ZeroTime;
            var result = _testDatabase.QueryModified(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(cursor.uniqueTime == 0);

            // Do a double check that even if the timestamp is "everything forever" then we still get nothing.
            result = _testDatabase.QueryModified(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(cursor.uniqueTime == 0);
        }


        /// <summary>
        /// This tests a typical day in a cursor user's day. A good example of standard cursor usage.
        /// </summary>
        [Test]
        public void CursorsModified02Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-modified-06.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);


            UnixTimeUtcUnique cursor = UnixTimeUtcUnique.ZeroTime;
            var result = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);

            // Modify one item make sure we can get it.
            _testDatabase.TblMainIndex.TestTouch(f2);
            result = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            // Debug.Assert(ByteArrayUtil.muidcmp(cursor, f2.ToByteArray()) == 0);

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


            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

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

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 0, 3, null, null);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            var result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing has been modified

            _testDatabase.TblMainIndex.TestTouch(f1);
            _testDatabase.TblMainIndex.TestTouch(f2);
            _testDatabase.TblMainIndex.TestTouch(f3);
            _testDatabase.TblMainIndex.TestTouch(f4);
            _testDatabase.TblMainIndex.TestTouch(f5);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Ensure everything is now "modified"

            outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            result = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 2, end: 3));
            Debug.Assert(result.Count == 3);
        }

        [Test]
        public void SecurityGroupAndAclBatch01Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-sgacl-01.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var a1 = SequentialGuid.CreateGuid();
            var a2 = SequentialGuid.CreateGuid();
            var a3 = SequentialGuid.CreateGuid();
            var a4 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 0, requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a1 }, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 0, requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a2 }, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 0, requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a1, a2 }, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 0, requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a3, a4 }, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 0, requiredSecurityGroup: 2, accessControlList: null, null);

            QueryBatchCursor cursor = null;

            // For any security group, we should have 5 entries
            cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);

            // For any security group, and an ACL, then the OR statement ignores the ACL, we should still have 5 entries
            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 5);

            // For NO valid security group, and a valid ACL, just the valid ACLs
            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 2);

            // For just security Group 1 we have 2 entries
            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 2);

            // For security Group 1 or any of the ACLs a1 we have 3
            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 3);

            // For security Group 1 or any of the ACLs a3, a4 we have 3
            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a3, a4 });
            Debug.Assert(result.Count == 3);

            // For no security Group 1 getting ACLs a1we have 2
            cursor = null;
            result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 2);
        }


        [Test]
        // Test we can add one and retrieve it
        public void GlobalTransitId01Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-gtri-01.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.GlobalTransitId, g1) == 0);
        }

        [Test]
        // Test we can add two and retrieve them
        public void GlobalTransitId02Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-gtri-02.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 0, 1, null, null);

            var f2 = SequentialGuid.CreateGuid();
            var g2 = Guid.NewGuid();
            _testDatabase.AddEntry(f2, g2, 1, 1, s1, t1, null, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            var data = _testDatabase.TblMainIndex.Get(f2);
            Debug.Assert(ByteArrayUtil.muidcmp(data.GlobalTransitId, g2) == 0);

            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f1) == 0);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.GlobalTransitId, g1) == 0);
        }


        [Test]
        // Test that we cannot add a duplicate
        public void GlobalTransitId03Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-gtri-03.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, g1, 1, 1, s1.ToByteArray(), t1, null, 0, 1, null, null);

            try
            {
                var f2 = SequentialGuid.CreateGuid();
                _testDatabase.AddEntry(f2, g1, 1, 1, s1.ToByteArray(), t1, null, 0, 1, null, null);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }


        [Test]
        // Test we can handle NULL
        public void GlobalTransitId04Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-gtri-04.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, null, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(data.GlobalTransitId == null);
        }


        [Test]
        // Test we can add one and retrieve it searching for a specific GTID guid
        public void GlobalTransitId05Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-gtri-05.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            // We shouldn't be able to find any like this:
            var result = _testDatabase.QueryBatch(1, ref cursor, globalTransitIdAnyOf: new List<Guid>() { t1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);

            // Now we should be able to find it
            result = _testDatabase.QueryBatch(1, ref cursor, globalTransitIdAnyOf: new List<Guid>() { t1, g1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            _testDatabase.TblMainIndex.TestTouch(f1); // Make sure we can find it
            result = _testDatabase.QueryModified(1, ref outCursor, globalTransitIdAnyOf: new List<Guid>() { t1, g1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
        }


        [Test]
        // Test we can modify the global transit guid with both update versions
        public void GlobalTransitId06Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-gtri-06.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var g3 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 0, 1, null, null);

            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.GlobalTransitId, g1) == 0);

            _testDatabase.UpdateEntry(f1, globalTransitId: g2);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.GlobalTransitId, g2) == 0);

            _testDatabase.UpdateEntryZapZap(f1, globalTransitId: g3);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.GlobalTransitId, g3) == 0);
        }



        [Test]
        // Test we can add one and retrieve it
        public void UniqueId01Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-uqid-01.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);
        }

        [Test]
        // Test we can add two and retrieve them
        public void UniqueId02Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-uqid-02.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 0, 1, null, null);

            var f2 = SequentialGuid.CreateGuid();
            var u2 = Guid.NewGuid();
            _testDatabase.AddEntry(f2, null, 1, 1, s1, t1, u2, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            var data = _testDatabase.TblMainIndex.Get(f2);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u2) == 0);

            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f1) == 0);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);
        }


        [Test]
        // Test that we cannot add a duplicate
        public void UniqueId03Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-uqid-03.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, null, 1, 1, s1.ToByteArray(), t1, u1, 0, 1, null, null);

            try
            {
                var f2 = SequentialGuid.CreateGuid();
                _testDatabase.AddEntry(f2, null, 1, 1, s1.ToByteArray(), t1, u1, 0, 1, null, null);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }


        [Test]
        // Test we can handle NULL
        public void UniqueId04Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-uqid-04.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, null, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var result = _testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(data.uniqueId == null);
        }


        [Test]
        // Test we can add one and retrieve it searching for a specific GTID guid
        public void UniqueId05Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-uqid-05.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 0, 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            // We shouldn't be able to find any like this:
            var result = _testDatabase.QueryBatch(1, ref cursor, uniqueIdAnyOf: new List<Guid>() { t1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);

            // Now we should be able to find it
            result = _testDatabase.QueryBatch(1, ref cursor, uniqueIdAnyOf: new List<Guid>() { t1, u1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            _testDatabase.TblMainIndex.TestTouch(f1); // Make sure we can find it
            result = _testDatabase.QueryModified(1, ref outCursor, uniqueIdAnyOf: new List<Guid>() { t1, u1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
        }


        [Test]
        // Test we can modify the global transit guid with both update versions
        public void UniqueId06Test()
        {
            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\driveIndexDB-uqid-06.db", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var u2 = Guid.NewGuid();
            var u3 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 0, 1, null, null);

            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);

            _testDatabase.UpdateEntry(f1, uniqueId: u2);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u2) == 0);

            _testDatabase.UpdateEntryZapZap(f1, uniqueId: u3);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u3) == 0);
        }



        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        public void UpdateTest()
        {
            var (testDatabase, fileId, conversationId, aclMembers, tags) = this.Init("update_entry_test.db");

            var _acllist = testDatabase.TblAclIndex.Get(fileId[0]);
            var _taglist = testDatabase.TblTagIndex.Get(fileId[0]);

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

            testDatabase.UpdateEntry(fileId[0], requiredSecurityGroup: 44, addAccessControlList: acladd, deleteAccessControlList: acllist, addTagIdList: tagadd, deleteTagIdList: taglist);
            var acllistres = testDatabase.TblAclIndex.Get(fileId[0]);
            var taglistres = testDatabase.TblTagIndex.Get(fileId[0]);

            Debug.Assert(acllistres.Count == 1);
            Debug.Assert(taglistres.Count == 1);

            Debug.Assert(ByteArrayUtil.muidcmp(acllistres[0], acladd[0]) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(taglistres[0], tagadd[0]) == 0);

            // Fix it back to where [0] was
            testDatabase.UpdateEntry(fileId[0], addAccessControlList: acllist, deleteAccessControlList: acladd, addTagIdList: taglist, deleteTagIdList: tagadd);
            acllistres = testDatabase.TblAclIndex.Get(fileId[0]);
            taglistres = testDatabase.TblTagIndex.Get(fileId[0]);
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
            // var cursorTimestamp = testDatabase.GetTimestamp();
            QueryBatchCursor cursor = null;

            var result = testDatabase.QueryBatch(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[fileId.Count - 1]) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[399], fileId[fileId.Count - 400]) == 0);

            var md = testDatabase.TblMainIndex.Get(fileId[0]);

            var p1 = testDatabase.TblAclIndex.Get(fileId[0]);
            Debug.Assert(p1 != null);
            Debug.Assert(p1.Count == 4);

            var p2 = testDatabase.TblTagIndex.Get(fileId[0]);
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

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            // Now let's be sure that there are no modified items. 0 gets everything that was ever modified
            result = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);

            var theguid = conversationId[42];
            testDatabase.UpdateEntry(fileId[420], fileType: 5, dataType: 6, senderId: conversationId[42].ToByteArray(), groupId: theguid, userDate: 42, requiredSecurityGroup: 333);

            // Now check that we can find the one modified item with our cursor timestamp
            result = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[420]) == 0);

            md = testDatabase.TblMainIndex.Get(fileId[420]);
            Debug.Assert(md.FileType == 5);
            Debug.Assert(md.DataType == 6);
            Debug.Assert(md.UserDate == 42);

            Assert.True(md.RequiredSecurityGroup == 333);

            // UInt64 tmpCursor = UnixTime.UnixTimeMillisecondsUnique();
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
                Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[fileId.Count - 1]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            result = testDatabase.QueryBatch(1, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            if (testDatabase.GetKind() == DatabaseIndexKind.TimeSeries)
            {
                Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[fileId.Count - 2]) == 0);
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
                tagsAnyOf: new List<Guid>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);


            //
            // Test that we can find a row with Acls. We know row 0 has acl 0..3
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);


            //
            // Test that we can find a row with ALL Tags listed. One, two and three. 
            // From three on it's a repeat code.
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            cursor = null;
            result = testDatabase.QueryBatch(1, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can execute a query with all main attributes set
            //
            cursor = null;
            result = testDatabase.QueryBatch(10,
                ref cursor,
                filetypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                datatypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                senderidAnyOf: new List<byte[]>() { tags[0].ToByteArray() },
                groupIdAnyOf: new List<Guid>() { tags[0] },
                userdateSpan: new UnixTimeUtcRange(new UnixTimeUtc(7), new UnixTimeUtc(42)),
                requiredSecurityGroup: allIntRange);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            result = testDatabase.QueryBatch(10, ref cursor,
                tagsAnyOf: new List<Guid>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] },
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            result = testDatabase.QueryBatch(100, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] },
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(result.Count < 100);
        }

        private (DriveIndexDatabase, List<Guid> _fileId, List<Guid> _ConversationId, List<Guid> _aclMembers, List<Guid> _Tags) Init(string filename)
        {
            var fileId = new List<Guid>();
            var conversationId = new List<Guid>();
            var aclMembers = new List<Guid>();
            var tags = new List<Guid>();

            Utils.DummyTypes(fileId, 1000);
            Utils.DummyTypes(conversationId, 1000);
            Utils.DummyTypes(aclMembers, 1000);
            Utils.DummyTypes(tags, 1000);

            DriveIndexDatabase _testDatabase = new DriveIndexDatabase($"URI=file:.\\{filename}", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

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

            _testDatabase.AddEntry(fileId[0], Guid.NewGuid(), 0, 0, conversationId[0].ToByteArray(), null, null, 0, 55, tmpacllist, tmptaglist);

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

                _testDatabase.AddEntry(fileId[i], Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), conversationId[myRnd.Next(0, conversationId.Count - 1)].ToByteArray(), null, null, 0, 55, tmpacllist, tmptaglist);
            }

            _testDatabase.Commit();

            stopWatch.Stop();
            Utils.StopWatchStatus($"Added {countMain + countAcl + countTags} rows: mainindex {countMain};  ACL {countAcl};  Tags {countTags}", stopWatch);

            return (_testDatabase, fileId, conversationId, aclMembers, tags);
        }
    }
}
