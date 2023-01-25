using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.ServerDatabase;

namespace IndexerTests.KeyValue
{
    public class TableCronTests
    {
        [Test]
        public void InsertCron01Test()
        {
            using var db = new ServerDatabase("URI=file:.\\cron-upsert-01.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(c1, 1, d1);
            var t2 = new UnixTimeUtc();

            var i = db.tblCron.Get(c1, 1);
            Assert.True(ByteArrayUtil.muidcmp(c1, i.identityGuid) == 0);
            Assert.True(i.type == 1);
            Assert.True(i.runCount == 0);
            Assert.True((i.nextRun >= t1) && (i.nextRun <= t2));
            Assert.True(i.lastRun.milliseconds == 0);
            Assert.True(i.popStamp == null);
            Assert.True(ByteArrayUtil.EquiByteArrayCompare(d1, i.data));

            Debug.Assert(true);
        }


        [Test]
        public void UpsertCron02Test()
        {
            using var db = new ServerDatabase("URI=file:.\\cron-upsert-02.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(c1, 1, d1);
            var t2 = new UnixTimeUtc();

            var i = db.tblCron.Get(c1, 1);
            Assert.True(ByteArrayUtil.muidcmp(c1, i.identityGuid) == 0);
            Assert.True(i.type == 1);
            Assert.True(i.runCount == 0);
            Assert.True((i.nextRun >= t1) && (i.nextRun <= t2));
            Assert.True(i.lastRun.milliseconds == 0);
            Assert.True(i.popStamp == null);
            Assert.True(ByteArrayUtil.EquiByteArrayCompare(d1, i.data));

            var d2 = Guid.NewGuid().ToByteArray();
            t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(c1, 1, d2);
            t2 = new UnixTimeUtc();

            i = db.tblCron.Get(c1, 1);
            Assert.True(ByteArrayUtil.muidcmp(c1, i.identityGuid) == 0);
            Assert.True(i.type == 1);
            Assert.True(i.runCount == 0);
            Assert.True((i.nextRun >= t1) && (i.nextRun <= t2));
            Assert.True(i.lastRun.milliseconds == 0);
            Assert.True(i.popStamp == null);
            Assert.True(ByteArrayUtil.EquiByteArrayCompare(d2, i.data));

            // Need one test like this but where I pop it first

            Debug.Assert(true);
        }


        [Test]
        public void UpsertCron03Test()
        {
            using var db = new ServerDatabase("URI=file:.\\cron-upsert-03.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(c1, 1, d1);
            var t2 = new UnixTimeUtc();

            var il = db.tblCron.Pop(10, out var popStamp);
            Assert.True(il.Count == 1);

            var i = db.tblCron.Get(c1, 1);
            Assert.True(ByteArrayUtil.muidcmp(il[0].identityGuid, i.identityGuid) == 0);
            Assert.True(i.type == il[0].type);
            Assert.True(i.runCount == il[0].runCount);
            Assert.True(i.lastRun == il[0].lastRun);
            Assert.True(ByteArrayUtil.muidcmp(i.popStamp, il[0].popStamp) == 0);
            Assert.True(ByteArrayUtil.EquiByteArrayCompare(il[0].data, i.data));
            Assert.True(i.nextRun == il[0].nextRun);
        }


        [Test]
        public void InsertCron04Test()
        {
            using var db = new ServerDatabase("URI=file:.\\cron-upsert-04.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(c1, 1, d1);
            db.tblCron.UpsertRow(c2, 1, d1);
            var t2 = new UnixTimeUtc();

            var i1 = db.tblCron.Get(c1, 1);
            Assert.True(ByteArrayUtil.muidcmp(c1, i1.identityGuid) == 0);
            Assert.True(i1.type == 1);
            Assert.True(i1.runCount == 0);
            Assert.True((i1.nextRun >= t1) && (i1.nextRun <= t2));
            Assert.True(i1.lastRun.milliseconds == 0);
            Assert.True(i1.popStamp == null);
            Assert.True(ByteArrayUtil.EquiByteArrayCompare(d1, i1.data));

            var i2 = db.tblCron.Get(c2, 1);
            Assert.True(ByteArrayUtil.muidcmp(c2, i2.identityGuid) == 0);
            Assert.True(i2.type == 1);
            Assert.True(i2.runCount == 0);
            Assert.True((i2.nextRun >= t1) && (i2.nextRun <= t2));
            Assert.True(i2.lastRun.milliseconds == 0);
            Assert.True(i2.popStamp == null);
            Assert.True(ByteArrayUtil.EquiByteArrayCompare(d1, i2.data));

            Debug.Assert(true);
        }



        [Test]
        public void PopCron01Test()
        {
            using var db = new ServerDatabase("URI=file:.\\pop-cron-01.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid().ToByteArray();
            var i1 = Guid.NewGuid();
            var i2 = Guid.NewGuid();

            db.tblCron.UpsertRow(i1, 1, d1);
            db.tblCron.UpsertRow(i2, 1, d1);

            var il1 = db.tblCron.Pop(1, out var popStamp1);
            Assert.True(il1.Count == 1);

            var il2 = db.tblCron.Pop(1, out var popStamp2);
            Assert.True(il2.Count == 1);

            // Making sure that the first item on the stack is the first item to get out
            Assert.True(il1[0].identityGuid == i1);
            Assert.True(il2[0].identityGuid == i2);
        }


        [Test]
        public void CronRecoverDead02Test()
        {
            using var db = new ServerDatabase("URI=file:.\\pop-cron-02.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid().ToByteArray();

            var t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            var t2 = new UnixTimeUtc();

            var il1 = db.tblCron.Pop(10, out var popStamp1);
            Assert.True(il1.Count == 3);

            // JIC you're wondering about the sleep. In case the timestamp is the same ms as the Pop
            // then it might not turn out right. RecoverDead() is designed to recover old stuff, not
            // by the ms, so I don't care.  Just need a pause to ensure the test always passes.
            //
            Thread.Sleep(1);
            var t3 = new UnixTimeUtc();

            var il2 = db.tblCron.Pop(10, out var _);
            Assert.True(il2.Count == 0);

            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);

            il1 = db.tblCron.Pop(10, out var popStamp2);
            Assert.True(il1.Count == 2);
            var t4 = new UnixTimeUtc();

            il2 = db.tblCron.Pop(10, out var _);
            Assert.True(il2.Count == 0);

            db.tblCron.PopRecoverDead(t3);

            il1 = db.tblCron.Pop(10, out var popStamp3);
            Assert.True(il1.Count == 3);

            Thread.Sleep(1);   // ms pause here too.
            var t5 = new UnixTimeUtc();
            db.tblCron.PopRecoverDead(t5);

            il1 = db.tblCron.Pop(10, out var popStamp5);
            Assert.True(il1.Count == 5);
        }


        [Test]
        public void CronCommit01Test()
        {
            using var db = new ServerDatabase("URI=file:.\\pop-commit-01.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid().ToByteArray();

            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);

            var il1 = db.tblCron.Pop(10, out var popStamp1);
            Assert.True(il1.Count == 3);

            db.tblCron.PopCommitList(new List<Guid>() { il1[0].identityGuid, il1[1].identityGuid }) ;

            Thread.Sleep(1);

            db.tblCron.PopRecoverDead(UnixTimeUtc.Now());

            var il2 = db.tblCron.Pop(10, out var _);
            Assert.True(il2.Count == 1);
        }


        [Test]
        public void CronCancel01Test()
        {
            using var db = new ServerDatabase("URI=file:.\\pop-cancel-01.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid().ToByteArray();

            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);
            db.tblCron.UpsertRow(Guid.NewGuid(), 1, d1);

            var il1 = db.tblCron.Pop(10, out var popStamp1);
            Assert.True(il1.Count == 3);

            db.tblCron.PopCancelList(new List<Guid>() { il1[0].identityGuid, il1[1].identityGuid });

            Thread.Sleep(1);

            db.tblCron.PopRecoverDead(UnixTimeUtc.Now());

            var il2 = db.tblCron.Pop(10, out var _);
            Assert.True(il2.Count == 3);
        }


        [Test]
        public void CronTimer01Test()
        {
            using var db = new ServerDatabase("URI=file:.\\pop-timer-01.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var t1 = new UnixTimeUtc();
            db.tblCron.UpsertRow(c1, 1, d1);
            var t2 = new UnixTimeUtc();

            // Check item data
            var i = db.tblCron.Get(c1, 1);
            Assert.True(i.runCount == 0);
            Assert.True((i.nextRun >= t1) && (i.nextRun <= t2));

            // Pop it, now counters are incremented
            var il1 = db.tblCron.Pop(1, out var _);
            Assert.True(il1.Count == 1);

            // re-check item data
            i = db.tblCron.Get(c1, 1);
            Assert.True(i.runCount == 1);
            Assert.True((i.nextRun.milliseconds >= t1.milliseconds+50000) && (i.nextRun.milliseconds <= t2.milliseconds+70000));

            db.tblCron.PopCancelList(new List<Guid>() { il1[0].identityGuid });


            // Pop it, now counters are incremented
            il1 = db.tblCron.Pop(1, out var _);
            Assert.True(il1.Count == 1);

            // re-check item data
            i = db.tblCron.Get(c1, 1);
            Assert.True(i.runCount == 2);
            Assert.True((i.nextRun.milliseconds >= t1.milliseconds + 110000) && (i.nextRun.milliseconds <= t2.milliseconds + 130000));

            db.tblCron.PopCancelList(new List<Guid>() { il1[0].identityGuid });


            // Pop it, now counters are incremented
            il1 = db.tblCron.Pop(1, out var _);
            Assert.True(il1.Count == 1);

            // re-check item data
            i = db.tblCron.Get(c1, 1);
            Assert.True(i.runCount == 3);
            Assert.True((i.nextRun.milliseconds >= t1.milliseconds + 230000) && (i.nextRun.milliseconds <= t2.milliseconds + 250000));

        }
    }
}
