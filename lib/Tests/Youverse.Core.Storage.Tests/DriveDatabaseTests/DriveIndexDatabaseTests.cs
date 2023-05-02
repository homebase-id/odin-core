﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.Sqlite.DriveDatabase;
using Youverse.Core.Util;

namespace DriveDatabaseTests
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
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            QueryBatchCursor cursor = null;

            // Do twice on each to ensure nothing changes state wise

            var (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.stopAtBoundary == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(moreRows == false);

            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.stopAtBoundary == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(10, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(outCursor.uniqueTime == 0);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            (result, moreRows) = _testDatabase.QueryModified(10, ref outCursor, requiredSecurityGroup: allIntRange);
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
        public void CursorsBatch02Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            QueryBatchCursor cursor = null;

            var (result, moreRows) = _testDatabase.QueryBatchAuto(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Check we got everything, we are done because result.Count < 100
            Debug.Assert(moreRows == false);

            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[4], f1) == 0);

            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0].ToByteArray(), cursor.stopAtBoundary) == 0);

            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            (result, moreRows) = _testDatabase.QueryBatchAuto(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(moreRows == false);


            // We do a refresh a few seconds later and since no new items have hit the DB nothing more is returned
            (result, moreRows) = _testDatabase.QueryBatchAuto(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);
        }

        /// <summary>
        /// Scenario: Chat client gets everything but in three batches (3 pages out of 5 items)
        /// subsequent queries. Again, newest chat items returned in the first query, etc.
        /// </summary>
        [Test]
        public void CursorsBatch03Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.stopAtBoundary == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f4.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(moreRows == true);

            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.stopAtBoundary == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f2.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(moreRows == true);

            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);
        }

        /// <summary>
        /// Scenario: We get the entire chat history. Then two new items are added. We check to get those.
        /// </summary>
        [Test]
        public void CursorsBatch04Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);


            // Now there should be no more items
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Add two more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f7, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            // Later we do a new query, with a NULL startFromCursor, because then we'll get the newest items first.
            // But stop at stopAtBoundaryCursor: pagingCursor
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Now there should be no more items
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Double check
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);
        }


        [Test]
        public void CursorsBatch05Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            QueryBatchCursor cursor = null;

            // How you'd read the entire DB in chunks in a for loop
            int c = 0;
            bool moreRows = false;
            List<Guid> result;
            for (int i = 1; i < 100; i++)
            {
                (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 5);
            Debug.Assert(moreRows == false);

            // Add two more items
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 43, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 43, new UnixTimeUtc(0), 1, null, null);

            // How you'd get the latest items (in chuinks) since your last update
            c = 0;
            for (int i = 1; i < 100; i++)
            {
                (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 2);
            Debug.Assert(moreRows == false);

            // Add five more items
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 44, new UnixTimeUtc(0), 0, null, null);

            // How you'd get the latest items (in chuinks) since your last update
            c = 0;
            for (int i = 1; i < 100; i++)
            {
                (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
                c += result.Count;
                if (result.Count == 0)
                    break;
            }

            Debug.Assert(c == 5);
            Debug.Assert(moreRows == false);
        }

        [Test]
        public void ArchivalStatusTest()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 0, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 1, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 1, new UnixTimeUtc(0), 3, null, null);
            _testDatabase.AddEntry(SequentialGuid.CreateGuid(), Guid.NewGuid(), 1, 1, s1, t1, null, 2, new UnixTimeUtc(0), 0, null, null);

            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 6);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange, archivalStatusAnyOf: new List<Int32>() { 0 });
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange, archivalStatusAnyOf: new List<Int32>() { 1 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange, archivalStatusAnyOf: new List<Int32>() { 2 });
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, archivalStatusAnyOf: new List<Int32>() { 0,1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique c2 = new UnixTimeUtcUnique(0);
            (result, moreRows) = _testDatabase.QueryModified(10, ref c2, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            _testDatabase.UpdateEntryZapZap(f1, archivalStatus: 7);

            c2 = new UnixTimeUtcUnique(0);
            (result, moreRows) = _testDatabase.QueryModified(10, ref c2, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, archivalStatusAnyOf: new List<Int32>() { 0 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            _testDatabase.UpdateEntry(f2, archivalStatus: 7);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, archivalStatusAnyOf: new List<Int32>() { 0 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
        }


        /// <summary>
        /// Scenario: Test the cursor behavior when you get exactly the limit set & there is new data.
        /// </summary>
        [Test]
        public void CursorsBatch06Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Add two more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f7, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            // Now there should be no more items (recursive call in QueryBatch())
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Now there should be no more items (recursive call in QueryBatch())
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.stopAtBoundary) == 0);
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
        public void CursorsBatch07ExampleTest()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            // Add five items to the chat database
            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            // Get everything from the chat database
            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[2], f3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[3], f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[4], f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(moreRows == false);

            // Now there should be no more items
            (result, moreRows) = _testDatabase.QueryBatchAuto(10, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(moreRows == false);

            // Now add three more items
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            var f8 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f7, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f8, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            // Now we get two of the three new items, we get the newest first f8 & f7
            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f8) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f7) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f7.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f5.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f8.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(moreRows == true);


            // Now add two more items
            var f9 = SequentialGuid.CreateGuid();
            var f10 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f9, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f10, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            // Now we get two more items. Internally, this will turn into two QueryBatchRaw()
            // because there is only 1 left in the previous range. A second request will get the
            // next item. Leaving us with 1 left over. The order of the items will be newest first,
            // so f10, f6. Note that you'll get a gap between {f8,f7,f6} and {f10,f9}, i.e. f9 still
            // waiting for the next query
            //
            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f10) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f6) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), cursor.pagingCursor) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f8.ToByteArray(), cursor.stopAtBoundary) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), cursor.nextBoundaryCursor) == 0);
            Debug.Assert(moreRows == true);

            // Now we get two more items, only one should be left (f9)
            (result, moreRows) = _testDatabase.QueryBatchAuto(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f9) == 0);
            Debug.Assert(moreRows == false);

            Debug.Assert(cursor.nextBoundaryCursor == null);
            Debug.Assert(cursor.pagingCursor == null);
            Debug.Assert(ByteArrayUtil.muidcmp(f10.ToByteArray(), cursor.stopAtBoundary) == 0);
        }


        [Test]
        public void QueryBatchCursorNewestHasRows01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
        }


        [Test]
        public void QueryBatchUserDateCursorNewestHasRows01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
        }


        [Test]
        public void QueryBatchCursorOldestHasRows01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
        }


        [Test]
        public void QueryBatchUserDateCursorOldestHasRows01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
        }


        [Test]
        public void QueryBatchCursorNewest01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);
        }

        [Test]
        public void QueryBatchUserDateCursorNewest01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(42), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f1) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);
            Debug.Assert(cursor.userDatePagingCursor.Value.milliseconds == 42);
        }


        [Test]
        public void QueryBatchCursorOldest01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f3.ToByteArray()) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f3.ToByteArray()) == 0);
        }


        [Test]
        public void QueryBatchUserDateCursorOldest01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(42), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null);

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(2, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == true);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f3.ToByteArray()) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f3) == 0);

            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f3.ToByteArray()) == 0);
        }


        [Test]
        public void QueryBatchCursorOldestNewest01()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            // Check we get the oldest and newest items

            QueryBatchCursor cursor = null;
            var (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f3) == 0);

            cursor = null;
            (result, hasRows) = _testDatabase.QueryBatch(1, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
        }


        [Test]
        public void TestQueryBatchStartPointGuid()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid(); 
            var f5 = SequentialGuid.CreateGuid(); 
            var f6 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            // Set the start point to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor();
            cursor.CursorStartPoint(f3.ToByteArray());

            // Get all the newest items. We should get f2, f1 and no more because f3 is the start point.
            var (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor.CursorStartPoint(f3.ToByteArray());

            // Get all the oldest items. We should get f4,f5,f6 because f3 is the start point and we're getting oldest first.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f6.ToByteArray()) == 0);
        }


        [Test]
        public void TestQueryBatchStartPointTime()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            Thread.Sleep(1);
            var t3 = UnixTimeUtc.Now();
            Thread.Sleep(1);
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            // Set the start point to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor();
            cursor.CursorStartPoint(t3, false);

            // Get all the newest items. We should get f2, f1 and no more because f3 is the start point.
            var (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f1.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor.CursorStartPoint(t3, false);

            // Get all the oldest items. We should get f4,f5,f6 because f3 is the start point and we're getting oldest first.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f6.ToByteArray()) == 0);
        }



        [Test]
        public void TestQueryBatchUserDateStartPointTime()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            Thread.Sleep(1);
            var t3 = UnixTimeUtc.Now();
            Thread.Sleep(1);
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(5000), 1, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(4000), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(3000), 2, null, null);
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 2, null, null);

            // Set the start point to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor();
            cursor.CursorStartPoint(new UnixTimeUtc(4000), true);

            // Get all the newest items. We should get f2, f1 and no more because f3 is the start point.
            var (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f5) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[2], f6) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor.CursorStartPoint(new UnixTimeUtc(4000), true);

            // Get all the oldest items. We should get f4,f5,f6 because f3 is the start point and we're getting oldest first.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, fileIdSort: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f2) == 0);
        }


        [Test]
        public void TestQueryBatchStopBoundaryGuid()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid(); 
            var f5 = SequentialGuid.CreateGuid(); 
            var f6 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            // Set the boundary item to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor(f3.ToByteArray());

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            var (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor = new QueryBatchCursor(f3.ToByteArray());

            // Get all the oldest items. We should get f1, f2 and no more because f3 is the boundary.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

        }



        [Test]
        public void TestQueryBatchStopBoundaryTime()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            Thread.Sleep(1);
            var t3 = UnixTimeUtc.Now();
            Thread.Sleep(1);
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid(); // Newest

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f6, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);

            // Set the boundary item to f3 (which we didn't put in the DB)
            var cursor = new QueryBatchCursor(t3, false);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            var (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 3);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: true, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f4.ToByteArray()) == 0);

            //
            // ====== Now do the same, oldest first
            //
            // Set the boundary item to f3 (which we didn't put in the DB)
            cursor = new QueryBatchCursor(t3, false);

            // Get all the oldest items. We should get f1, f2 and no more because f3 is the boundary.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

            // Get all the newest items. We should get f6,f5,f4 and no more because f3 is the boundary.
            (result, hasRows) = _testDatabase.QueryBatch(10, ref cursor, newestFirstOrder: false, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(hasRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(cursor.pagingCursor, f2.ToByteArray()) == 0);

        }

        /// <summary>
        /// Scenario: A newly installed chat client downloads the entire database. Gets everything in one go.
        /// Tests only the QueryBatch(). It's a new database, nothing is modified, so nothing gets back as modified.
        /// </summary>
        [Test]
        public void CursorsModified01Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            UnixTimeUtcUnique cursor = UnixTimeUtcUnique.ZeroTime;
            var (result, moreRows) = _testDatabase.QueryModified(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(cursor.uniqueTime == 0);
            Debug.Assert(moreRows == false);

            // Do a double check that even if the timestamp is "everything forever" then we still get nothing.
            (result, moreRows) = _testDatabase.QueryModified(100, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing in the DB should be modified
            Debug.Assert(cursor.uniqueTime == 0);
            Debug.Assert(moreRows == false);
        }


        /// <summary>
        /// This tests a typical day in a cursor user's day. A good example of standard cursor usage.
        /// </summary>
        [Test]
        public void CursorsModified02Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);


            UnixTimeUtcUnique cursor = UnixTimeUtcUnique.ZeroTime;
            var (result, moreRows) = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // Modify one item make sure we can get it.
            _testDatabase.TblMainIndex.TestTouch(f2);
            (result, moreRows) = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            // Debug.Assert(ByteArrayUtil.muidcmp(cursor, f2.ToByteArray()) == 0);
            Debug.Assert(moreRows == false);

            // Make sure cursor is updated and we're at the end
            (result, moreRows) = _testDatabase.QueryModified(2, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);
        }


        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        public void RequiredSecurityGroupBatch01Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();


            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            QueryBatchCursor cursor = null;

            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 4, end: 10));
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 2));
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);
        }


        [Test]
        public void RequiredSecurityGroupModified02Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.Random);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            var (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // Nothing has been modified
            Debug.Assert(moreRows == false);

            _testDatabase.TblMainIndex.TestTouch(f1);
            _testDatabase.TblMainIndex.TestTouch(f2);
            _testDatabase.TblMainIndex.TestTouch(f3);
            _testDatabase.TblMainIndex.TestTouch(f4);
            _testDatabase.TblMainIndex.TestTouch(f5);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5); // Ensure everything is now "modified"
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 0, end: 0));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 2, end: 2));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 3, end: 3));
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            outCursor = UnixTimeUtcUnique.ZeroTime;
            (result, moreRows) = _testDatabase.QueryModified(400, ref outCursor, requiredSecurityGroup: new IntRange(start: 2, end: 3));
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);
        }

        [Test]
        public void SecurityGroupAndAclBatch01Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
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

            _testDatabase.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a1 }, null);
            _testDatabase.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: new List<Guid>() { a2 }, null);
            _testDatabase.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a1, a2 }, null);
            _testDatabase.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: new List<Guid>() { a3, a4 }, null);
            _testDatabase.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, Guid.NewGuid(), 42, new UnixTimeUtc(0), requiredSecurityGroup: 2, accessControlList: null, null);

            QueryBatchCursor cursor = null;

            // For any security group, we should have 5 entries
            cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            // For any security group, and an ACL, then the OR statement ignores the ACL, we should still have 5 entries
            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange, aclAnyOf: new List<Guid>() { a4 });
            Debug.Assert(result.Count == 5);
            Debug.Assert(moreRows == false);

            // For NO valid security group, and a valid ACL, just the valid ACLs
            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // For just security Group 1 we have 2 entries
            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1));
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);

            // For security Group 1 or any of the ACLs a1 we have 3
            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

            // For security Group 1 or any of the ACLs a3, a4 we have 3
            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 1, end: 1), aclAnyOf: new List<Guid>() { a3, a4 });
            Debug.Assert(result.Count == 3);
            Debug.Assert(moreRows == false);

            // For no security Group 1 getting ACLs a1we have 2
            cursor = null;
            (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: new IntRange(start: 0, end: 0), aclAnyOf: new List<Guid>() { a1 });
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);
        }


        [Test]
        // Test we can add one and retrieve it
        public void GlobalTransitId01Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g1) == 0);
            Debug.Assert(moreRows == false);
        }

        [Test]
        // Test we can add two and retrieve them
        public void GlobalTransitId02Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            var f2 = SequentialGuid.CreateGuid();
            var g2 = Guid.NewGuid();
            _testDatabase.AddEntry(f2, g2, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f2) == 0);
            var data = _testDatabase.TblMainIndex.Get(f2);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g2) == 0);

            Debug.Assert(ByteArrayUtil.muidcmp(result[1], f1) == 0);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g1) == 0);
        }


        [Test]
        // Test that we cannot add a duplicate
        public void GlobalTransitId03Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, g1, 1, 1, s1.ToByteArray(), t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            try
            {
                var f2 = SequentialGuid.CreateGuid();
                _testDatabase.AddEntry(f2, g1, 1, 1, s1.ToByteArray(), t1, null, 42, new UnixTimeUtc(0), 1, null, null);
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
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(data.globalTransitId == null);
        }


        [Test]
        // Test we can add one and retrieve it searching for a specific GTID guid
        public void GlobalTransitId05Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            // We shouldn't be able to find any like this:
            var (result, moreRows) = _testDatabase.QueryBatchAuto(1, ref cursor, globalTransitIdAnyOf: new List<Guid>() { t1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // Now we should be able to find it
            (result, moreRows) = _testDatabase.QueryBatchAuto(1, ref cursor, globalTransitIdAnyOf: new List<Guid>() { t1, g1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            _testDatabase.TblMainIndex.TestTouch(f1); // Make sure we can find it
            (result, moreRows) = _testDatabase.QueryModified(1, ref outCursor, globalTransitIdAnyOf: new List<Guid>() { t1, g1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
        }


        [Test]
        // Test we can modify the global transit guid with both update versions
        public void GlobalTransitId06Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var g3 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, g1, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g1) == 0);

            _testDatabase.UpdateEntry(f1, globalTransitId: g2, archivalStatus: 7);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g2) == 0);

            _testDatabase.UpdateEntryZapZap(f1, globalTransitId: g3);
            data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.globalTransitId, g3) == 0);
        }



        [Test]
        // Test we can add one and retrieve it
        public void UniqueId01Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(ByteArrayUtil.muidcmp(data.uniqueId, u1) == 0);
        }

        [Test]
        // Test we can add two and retrieve them
        public void UniqueId02Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null);

            var f2 = SequentialGuid.CreateGuid();
            var u2 = Guid.NewGuid();
            _testDatabase.AddEntry(f2, null, 1, 1, s1, t1, u2, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 2);
            Debug.Assert(moreRows == false);
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
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid();
            var t1 = SequentialGuid.CreateGuid();
            _testDatabase.AddEntry(f1, null, 1, 1, s1.ToByteArray(), t1, u1, 42, new UnixTimeUtc(0), 1, null, null);

            try
            {
                var f2 = SequentialGuid.CreateGuid();
                _testDatabase.AddEntry(f2, null, 1, 1, s1.ToByteArray(), t1, u1, 42, new UnixTimeUtc(0), 1, null, null);
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
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            var (result, moreRows) = _testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], f1) == 0);
            var data = _testDatabase.TblMainIndex.Get(f1);
            Debug.Assert(data.uniqueId == null);
        }


        [Test]
        // Test we can add one and retrieve it searching for a specific GTID guid
        public void UniqueId05Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null);

            QueryBatchCursor cursor = null;
            cursor = null;
            // We shouldn't be able to find any like this:
            var (result, moreRows) = _testDatabase.QueryBatchAuto(1, ref cursor, uniqueIdAnyOf: new List<Guid>() { t1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // Now we should be able to find it
            (result, moreRows) = _testDatabase.QueryBatchAuto(1, ref cursor, uniqueIdAnyOf: new List<Guid>() { t1, u1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            _testDatabase.TblMainIndex.TestTouch(f1); // Make sure we can find it
            (result, moreRows) = _testDatabase.QueryModified(1, ref outCursor, uniqueIdAnyOf: new List<Guid>() { t1, u1 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == false);
        }


        [Test]
        // Test we can modify the global transit guid with both update versions
        public void UniqueId06Test()
        {
            using DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var u1 = Guid.NewGuid();
            var u2 = Guid.NewGuid();
            var u3 = Guid.NewGuid();
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();

            _testDatabase.AddEntry(f1, null, 1, 1, s1, t1, u1, 42, new UnixTimeUtc(0), 1, null, null);

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

            testDatabase.Dispose();
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

            var (result, moreRows) = testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[fileId.Count - 1]) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(result[399], fileId[fileId.Count - 400]) == 0);
            Debug.Assert(moreRows == true);

            var md = testDatabase.TblMainIndex.Get(fileId[0]);

            var p1 = testDatabase.TblAclIndex.Get(fileId[0]);
            Debug.Assert(p1 != null);
            Debug.Assert(p1.Count == 4);

            var p2 = testDatabase.TblTagIndex.Get(fileId[0]);
            Debug.Assert(p2 != null);
            Debug.Assert(p2.Count == 4);


            (result, moreRows) = testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 400);
            Debug.Assert(moreRows == true);

            (result, moreRows) = testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 200); // We put 1,000 lines into the index. 400+400+200 = 1,000
            Debug.Assert(moreRows == false);

            stopWatch.Stop();
            Utils.StopWatchStatus("Built in QueryBatch()", stopWatch);

            // Try to get a batch stopping at boundaryCursor. We should get none.
            (result, moreRows) = testDatabase.QueryBatchAuto(400, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0); // There should be no more
            Debug.Assert(moreRows == false);

            UnixTimeUtcUnique outCursor = UnixTimeUtcUnique.ZeroTime;
            // Now let's be sure that there are no modified items. 0 gets everything that was ever modified
            (result, moreRows) = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            var theguid = conversationId[42];
            testDatabase.UpdateEntry(fileId[420], fileType: 5, dataType: 6, senderId: conversationId[42].ToByteArray(), groupId: theguid, userDate: new UnixTimeUtc(42), requiredSecurityGroup: 333);

            // Now check that we can find the one modified item with our cursor timestamp
            (result, moreRows) = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[420]) == 0);
            Debug.Assert(moreRows == false);

            md = testDatabase.TblMainIndex.Get(fileId[420]);
            Debug.Assert(md.fileType == 5);
            Debug.Assert(md.dataType == 6);
            Debug.Assert(md.userDate == new UnixTimeUtc(42));

            Assert.True(md.requiredSecurityGroup == 333);

            // UInt64 tmpCursor = UnixTime.UnixTimeMillisecondsUnique();
            // Now check that we can't find the one modified item with a newer cursor 
            (result, moreRows) = testDatabase.QueryModified(100, ref outCursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 0);
            Debug.Assert(moreRows == false);

            // KIND : TimeSeries
            // Test that if we fetch the first record, it is the latest fileId
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(1, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == true);

            if (testDatabase.GetKind() == DatabaseIndexKind.TimeSeries)
            {
                Debug.Assert(ByteArrayUtil.muidcmp(result[0], fileId[fileId.Count - 1]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            (result, moreRows) = testDatabase.QueryBatchAuto(1, ref cursor, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count == 1);
            Debug.Assert(moreRows == true);
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
            (result, moreRows) = testDatabase.QueryBatchAuto(1, ref cursor, filetypesAnyOf: new List<int>() { 0, 4 }, requiredSecurityGroup: allIntRange);
            Debug.Assert(moreRows == true);
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Tags. We know row 0 has tag 0..3
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(100, ref cursor,
                tagsAnyOf: new List<Guid>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);


            //
            // Test that we can find a row with Acls. We know row 0 has acl 0..3
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(1, ref cursor,
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == true);


            //
            // Test that we can find a row with ALL Tags listed. One, two and three. 
            // From three on it's a repeat code.
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(100, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(100, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(1, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1], tags[2] }, requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            //
            // Test that we can execute a query with all main attributes set
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(10,
                ref cursor,
                filetypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                datatypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                senderidAnyOf: new List<byte[]>() { tags[0].ToByteArray() },
                groupIdAnyOf: new List<Guid>() { tags[0] },
                userdateSpan: new UnixTimeUtcRange(new UnixTimeUtc(7), new UnixTimeUtc(42)),
                requiredSecurityGroup: allIntRange);
            Debug.Assert(moreRows == false);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(100, ref cursor,
                tagsAnyOf: new List<Guid>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] },
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(moreRows == false);

            //
            // Test that we can find a row with Acls AND Tags
            //
            cursor = null;
            (result, moreRows) = testDatabase.QueryBatchAuto(100, ref cursor,
                tagsAllOf: new List<Guid>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<Guid>() { aclMembers[0], aclMembers[1], aclMembers[2] },
                requiredSecurityGroup: allIntRange);
            Debug.Assert(result.Count >= 1);
            Debug.Assert(result.Count < 100);
            Debug.Assert(moreRows == false);

            testDatabase.Dispose();
        }

        private (DriveDatabase, List<Guid> _fileId, List<Guid> _ConversationId, List<Guid> _aclMembers, List<Guid> _Tags) Init(string filename)
        {
            var fileId = new List<Guid>();
            var conversationId = new List<Guid>();
            var aclMembers = new List<Guid>();
            var tags = new List<Guid>();

            Utils.DummyTypes(fileId, 1000);
            Utils.DummyTypes(conversationId, 1000);
            Utils.DummyTypes(aclMembers, 1000);
            Utils.DummyTypes(tags, 1000);

            DriveDatabase _testDatabase = new DriveDatabase($"", DatabaseIndexKind.TimeSeries);
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

            _testDatabase.AddEntry(fileId[0], Guid.NewGuid(), 0, 0, conversationId[0].ToByteArray(), null, null, 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist);

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

                _testDatabase.AddEntry(fileId[i], Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), conversationId[myRnd.Next(0, conversationId.Count - 1)].ToByteArray(), null, null, 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist);
            }

            _testDatabase.Commit();

            stopWatch.Stop();
            Utils.StopWatchStatus($"Added {countMain + countAcl + countTags} rows: mainindex {countMain};  ACL {countAcl};  Tags {countTags}", stopWatch);

            return (_testDatabase, fileId, conversationId, aclMembers, tags);
        }
    }
}
