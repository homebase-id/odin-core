using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace IndexerTests.KeyValue
{
    public class TableCircleMemberTests
    {

        [Test]
        public void InsertInvalidIdTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-00.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();

            bool ok = false;
            try
            {
                db.tblCircleMember.AddMembers(null, new List<byte[]>() { m1 });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            var m2 = new byte[3]  { 17, 14, 15 };
            try
            {
                db.tblCircleMember.AddMembers(m2, new List<byte[]>() { m1 });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void InsertInvalidMemberTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-000.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();

            bool ok = false;
            try
            {
                db.tblCircleMember.AddMembers(c1, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            var m2 = new byte[TableCircleMember.MAX_MEMBER_LENGTH+1];
            try
            {
                db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m2 });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            var m3 = new byte[TableCircleMember.MAX_MEMBER_LENGTH];
            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m3 });
        }



        [Test]
        public void InsertTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-01.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();

            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1 });

            var m2 = Guid.NewGuid().ToByteArray();

            var r = db.tblCircleMember.GetMembers(c1);

            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0], m1) == 0);
        }


        [Test]
        public void InsertDuplicateTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-02.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();

            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1 });

            bool ok = false;
            try
            {
                db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1 });
            }
            catch
            {
                ok = true;
            }

            Debug.Assert(ok);
        }


        [Test]
        public void InsertEmptyTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-03.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();

            bool ok = false;
            try
            {
                db.tblCircleMember.AddMembers(c1, new List<byte[]>() { });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            try
            {
                db.tblCircleMember.AddMembers(c1, null);
            }
            catch
            {
                ok = true;
            }

            Debug.Assert(ok);
        }


        [Test]
        public void InsertMultipleMembersTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-04.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();
            var m2 = Guid.NewGuid().ToByteArray();
            var m3 = Guid.NewGuid().ToByteArray();

            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1, m2, m3 });

            var r = db.tblCircleMember.GetMembers(c1);

            Debug.Assert(r.Count == 3);
        }


        [Test]
        public void InsertMultipleCirclesTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-05.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var c2 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();
            var m2 = Guid.NewGuid().ToByteArray();
            var m3 = Guid.NewGuid().ToByteArray();
            var m4 = Guid.NewGuid().ToByteArray();
            var m5 = Guid.NewGuid().ToByteArray();

            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1, m2, m3 });
            db.tblCircleMember.AddMembers(c2, new List<byte[]>() { m2, m3, m4, m5 });

            var r = db.tblCircleMember.GetMembers(c1);
            Debug.Assert(r.Count == 3);
            r = db.tblCircleMember.GetMembers(c2);
            Debug.Assert(r.Count == 4);
        }

        [Test]
        public void RemoveMembersEmptyTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-10.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var c2 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();
            var m2 = Guid.NewGuid().ToByteArray();
            var m3 = Guid.NewGuid().ToByteArray();
            var m4 = Guid.NewGuid().ToByteArray();
            var m5 = Guid.NewGuid().ToByteArray();

            bool ok = false;

            try
            {
                db.tblCircleMember.RemoveMembers(c1, new List<byte[]>() { });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            try
            {
                db.tblCircleMember.RemoveMembers(c1, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void RemoveMembersTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-11.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var c2 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();
            var m2 = Guid.NewGuid().ToByteArray();
            var m3 = Guid.NewGuid().ToByteArray();
            var m4 = Guid.NewGuid().ToByteArray();
            var m5 = Guid.NewGuid().ToByteArray();

            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1, m2, m3 });
            db.tblCircleMember.AddMembers(c2, new List<byte[]>() { m2, m3, m4, m5 });
            db.tblCircleMember.RemoveMembers(c1, new List<byte[]>() { m1, m2 });

            var r = db.tblCircleMember.GetMembers(c1);
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0], m3) == 0);

            db.tblCircleMember.RemoveMembers(c2, new List<byte[]>() { m3, m4 });
            r = db.tblCircleMember.GetMembers(c2);
            Debug.Assert(r.Count == 2);
        }


        [Test]
        public void DeleteMembersEmptyTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-20.db");
            db.CreateDatabase();

            bool ok = false;
            try
            {
                db.tblCircleMember.DeleteMembers(new List<byte[]>() { });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            try
            {
                db.tblCircleMember.DeleteMembers(null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void DeleteMembersTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\circlemember-21.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var c2 = Guid.NewGuid().ToByteArray();
            var m1 = Guid.NewGuid().ToByteArray();
            var m2 = Guid.NewGuid().ToByteArray();
            var m3 = Guid.NewGuid().ToByteArray();
            var m4 = Guid.NewGuid().ToByteArray();
            var m5 = Guid.NewGuid().ToByteArray();

            db.tblCircleMember.AddMembers(c1, new List<byte[]>() { m1, m2, m3 });
            db.tblCircleMember.AddMembers(c2, new List<byte[]>() { m2, m3, m4, m5 });
            db.tblCircleMember.DeleteMembers(new List<byte[]>() { m1, m2 });

            var r = db.tblCircleMember.GetMembers(c1);
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0], m3) == 0);

            r = db.tblCircleMember.GetMembers(c2);
            Debug.Assert(r.Count == 3);
        }
    }
}
