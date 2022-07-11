using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    public class DriveIndexDatabaseTests
    {
        // The Init() seems slightly screwy. I think they'll end up in a race condition. Just guessing.
        [Test]
        public void UpdateTest()
        {
            var (testDatabase, fileId, conversationId, aclMembers, tags) = this.Init("update_entry_test.db");

            var acllist = testDatabase.TblAclIndex.Get(new Guid(fileId[0]));
            var taglist = testDatabase.TblTagIndex.Get(new Guid(fileId[0]));

            Debug.Assert(acllist.Count == 4);
            Debug.Assert(taglist.Count == 4);

            var acladd = new List<Guid>();
            var tagadd = new List<Guid>();
            acladd.Add(new Guid());
            tagadd.Add(new Guid());

            testDatabase.UpdateEntry(new Guid(fileId[0]), requiredSecurityGroup: 44, addAccessControlList: acladd, deleteAccessControlList: acllist, addTagIdList: tagadd, deleteTagIdList: taglist);
            var acllistres = testDatabase.TblAclIndex.Get(new Guid(fileId[0]));
            var taglistres = testDatabase.TblTagIndex.Get(new Guid(fileId[0]));

            Debug.Assert(acllistres.Count == 1);
            Debug.Assert(taglistres.Count == 1);

            Debug.Assert(SequentialGuid.muidcmp(acllistres[0].ToByteArray(), acladd[0].ToByteArray()) == 0);
            Debug.Assert(SequentialGuid.muidcmp(taglistres[0].ToByteArray(), tagadd[0].ToByteArray()) == 0);

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

            byte[] lastCursor;
            byte[] firstCursor;
            UInt64 cursorTimestamp;


            stopWatch.Start();

            //
            // Test fetching in batches work, cursors, counts
            //

            // For the first query, save the firstCursor
            var result = testDatabase.QueryBatch(400, out firstCursor, out lastCursor, out cursorTimestamp);
            Debug.Assert(lastCursor != null);
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


            result = testDatabase.QueryBatch(400, out _, out lastCursor, out _, startFromCursor: lastCursor);
            Debug.Assert(lastCursor != null);
            Debug.Assert(result.Count == 400);

            result = testDatabase.QueryBatch(400, out _, out lastCursor, out _, startFromCursor: lastCursor);
            Debug.Assert(lastCursor == null);
            Debug.Assert(result.Count == 200); // We put 1,000 lines into the index. 400+400+200 = 1,000

            stopWatch.Stop();
            Utils.StopWatchStatus("Built in QueryBatch()", stopWatch);

            // Try to get a batch stopping at firstCursor. We should get none.
            result = testDatabase.QueryBatch(400, out _, out lastCursor, out _, stopAtCursor: firstCursor);
            Debug.Assert(lastCursor == null);
            Debug.Assert(firstCursor != null);
            Debug.Assert(result.Count == 0); // There should be no more

            // Now let's be sure that there are no modified items. 0 gets everything that was ever modified
            result = testDatabase.QueryModified(100, out lastCursor, 0);
            Debug.Assert(result.Count == 0);

            testDatabase.UpdateEntry(new Guid(fileId[420]), fileType: 5, dataType: 6, senderId: conversationId[42], threadId: conversationId[42], userDate: 42, requiredSecurityGroup: 333);

            // Now check that we can find the one modified item with our cursor timestamp
            result = testDatabase.QueryModified(100, out lastCursor, cursorTimestamp);
            Debug.Assert(result.Count == 1);
            Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[420]) == 0);

            md = testDatabase.TblMainIndex.Get(new Guid(fileId[420]));
            Debug.Assert(md.FileType == 5);
            Debug.Assert(md.DataType == 6);
            Debug.Assert(md.UserDate == 42);

            Assert.True(md.RequiredSecurityGroup == 333);

            UInt64 tmpCursor = UnixTime.UnixTimeMillisecondsUnique();
            // Now check that we can't find the one modified item with a newer cursor 
            result = testDatabase.QueryModified(100, out lastCursor, tmpCursor);
            Debug.Assert(result.Count == 0);


            // KIND : TimeSeries
            // Test that if we fetch the first record, it is the latest fileId
            //
            result = testDatabase.QueryBatch(1, out _, out lastCursor, out _);
            Debug.Assert(result.Count == 1);

            if (testDatabase.GetKind() == DatabaseIndexKind.TimeSeries)
            {
                Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[fileId.Length - 1]) == 0);
                Debug.Assert(SequentialGuid.muidcmp(lastCursor, fileId[fileId.Length - 1]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            result = testDatabase.QueryBatch(1, out _, out lastCursor, out _, startFromCursor: lastCursor);
            Debug.Assert(result.Count == 1);
            if (testDatabase.GetKind() == DatabaseIndexKind.TimeSeries)
            {
                Debug.Assert(SequentialGuid.muidcmp(result[0], fileId[fileId.Length - 2]) == 0);
                Debug.Assert(SequentialGuid.muidcmp(lastCursor, fileId[fileId.Length - 2]) == 0);
            }
            else
            {
                throw new Exception("What to expect here?");
            }

            //
            // Test that fileType works. We know row #1 has filetype 0.
            //
            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _, startFromCursor: lastCursor, filetypesAnyOf: new List<int>() { 0, 4 });
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Tags. We know row 0 has tag 0..3
            //
            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _,
                tagsAnyOf: new List<byte[]>() { tags[0], tags[1], tags[2] });
            Debug.Assert(result.Count >= 1);


            //
            // Test that we can find a row with Acls. We know row 0 has acl 0..3
            //
            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _,
                aclAnyOf: new List<byte[]>() { aclMembers[0], aclMembers[1], aclMembers[2] });
            Debug.Assert(result.Count >= 1);


            //
            // Test that we can find a row with ALL Tags listed. One, two and three. 
            // From three on it's a repeat code.
            //
            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _,
                tagsAllOf: new List<byte[]>() { tags[0] });
            Debug.Assert(result.Count >= 1);

            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _,
                tagsAllOf: new List<byte[]>() { tags[0], tags[1] });
            Debug.Assert(result.Count >= 1);

            result = testDatabase.QueryBatch(1, out _, out lastCursor, out _,
                tagsAllOf: new List<byte[]>() { tags[0], tags[1], tags[2] });
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can execute a query with all main attributes set
            //
            result = testDatabase.QueryBatch(10,
                out _,
                out _,
                out _,
                filetypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                datatypesAnyOf: new List<int>() { 0, 1, 2, 3, 4, 5 },
                senderidAnyOf: new List<byte[]>() { tags[0] },
                threadidAnyOf: new List<byte[]>() { tags[0] },
                userdateSpan: new TimeRange() { Start = 7, End = 42 });

            //
            // Test that we can find a row with Acls AND Tags
            //
            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _,
                tagsAnyOf: new List<byte[]>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<byte[]>() { aclMembers[0], aclMembers[1], aclMembers[2] });
            Debug.Assert(result.Count >= 1);

            //
            // Test that we can find a row with Acls AND Tags
            //
            result = testDatabase.QueryBatch(10, out _, out lastCursor, out _,
                tagsAllOf: new List<byte[]>() { tags[0], tags[1], tags[2] },
                aclAnyOf: new List<byte[]>() { aclMembers[0], aclMembers[1], aclMembers[2] });
            Debug.Assert(result.Count >= 1);


            // TODO: Add three more rows, make sure we get them with firstCursor
            testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, conversationId[0], null, 0, 55, null, null);
            testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, conversationId[0], null, 0, 55, null, null);
            testDatabase.AddEntry(new Guid(SequentialGuid.CreateGuid()), 1, 1, conversationId[0], null, 0, 55, null, null);
            testDatabase.Commit();

            // Try to get a batch stopping at firstCursor. We should get none.
            result = testDatabase.QueryBatch(400, out firstCursor, out lastCursor, out _, stopAtCursor: firstCursor);
            Debug.Assert(lastCursor == null);
            Debug.Assert(result.Count == 3); // There should be no more

            // Make sure we're at the end again.
            result = testDatabase.QueryBatch(400, out _, out lastCursor, out _, stopAtCursor: firstCursor);
            Debug.Assert(lastCursor == null);
            Debug.Assert(result.Count == 0); // There should be no more
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
            var tmpacllist = new List<Guid>();
            tmpacllist.Add(new Guid(aclMembers[0]));
            tmpacllist.Add(new Guid(aclMembers[1]));
            tmpacllist.Add(new Guid(aclMembers[2]));
            tmpacllist.Add(new Guid(aclMembers[3]));

            var tmptaglist = new List<Guid>();
            tmptaglist.Add(new Guid(tags[0]));
            tmptaglist.Add(new Guid(tags[1]));
            tmptaglist.Add(new Guid(tags[2]));
            tmptaglist.Add(new Guid(tags[3]));

            _testDatabase.AddEntry(new Guid(fileId[0]), 0, 0, conversationId[0], null, 0, 55, tmpacllist, tmptaglist);

            // Insert a lot of random data
            for (var i = 0 + 1; i < fileId.Length; i++)
            {
                countMain++;

                tmpacllist = new List<Guid>();

                for (int j = 0, r = myRnd.Next(0, 5); j < r; j++)
                {
                    int rn = myRnd.Next(j + 1, seqAcl.Length - 1);
                    int xt = Utils.swap(ref seqAcl[j], ref seqAcl[rn]);
                    tmpacllist.Add(new Guid(aclMembers[seqAcl[j]]));
                    countAcl++;
                }

                tmptaglist = new List<Guid>();

                for (int j = 0, r = myRnd.Next(0, 5); j < r; j++)
                {
                    int rn = myRnd.Next(j + 1, seqTags.Length - 1);
                    int xt = Utils.swap(ref seqTags[j], ref seqTags[rn]);
                    tmptaglist.Add(new Guid(tags[seqTags[j]]));
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