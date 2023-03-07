﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace IdentityDatabaseTests
{
    public class TableCircleMemberTests
    {
        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var cl = new List<CircleMemberItem> { new CircleMemberItem() { circleId = c1, memberId =m1, data = d1 } };
            db.tblCircleMember.AddCircleMembers(cl);

            var r = db.tblCircleMember.GetCircleMembers(c1);

            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
        }


        [Test]
        public void InsertDuplicateTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();

            var cl = new List<CircleMemberItem> { new CircleMemberItem() { circleId = c1, memberId = m1, data = null } };
            db.tblCircleMember.AddCircleMembers(cl);

            bool ok = false;
            try
            {
                db.tblCircleMember.AddCircleMembers(cl);
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
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var cl = new List<CircleMemberItem> { new CircleMemberItem() { circleId = c1, memberId = m1, data = d1 } };

            bool ok = false;
            try
            {
                db.tblCircleMember.AddCircleMembers(new List<CircleMemberItem> ());
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
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();

            var cl = new List<CircleMemberItem> { 
                new CircleMemberItem() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m3, data = d1 } };

            db.tblCircleMember.AddCircleMembers(cl);

            var r = db.tblCircleMember.GetCircleMembers(c1);

            Debug.Assert(r.Count == 3);
        }


        [Test]
        public void InsertMultipleCirclesTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m3, data = d1 } };

            db.tblCircleMember.AddCircleMembers(cl);

            var cl2 = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m5, data = d1 }
            };
            db.tblCircleMember.AddCircleMembers(cl2);

            var r = db.tblCircleMember.GetCircleMembers(c1);
            Debug.Assert(r.Count == 3);
            r = db.tblCircleMember.GetCircleMembers(c2);
            Debug.Assert(r.Count == 4);
        }


        [Test]
        public void RemoveMembersTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m3, data = d1 } };

            db.tblCircleMember.AddCircleMembers(cl);

            var cl2 = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m5, data = d1 }
            };
            db.tblCircleMember.AddCircleMembers(cl2);

            db.tblCircleMember.RemoveCircleMembers(c1, new List<Guid>() { m1, m2 });

            var r = db.tblCircleMember.GetCircleMembers(c1);
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);

            db.tblCircleMember.RemoveCircleMembers(c2, new List<Guid>() { m3, m4 });
            r = db.tblCircleMember.GetCircleMembers(c2);
            Debug.Assert(r.Count == 2);
        }


        [Test]
        public void DeleteMembersEmptyTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            bool ok = false;
            try
            {
                db.tblCircleMember.DeleteMembersFromAllCircles(new List<Guid>() { });
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
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var m1 = Guid.NewGuid();
            var m2 = Guid.NewGuid();
            var m3 = Guid.NewGuid();
            var m4 = Guid.NewGuid();
            var m5 = Guid.NewGuid();

            var cl = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m3, data = d1 } };

            db.tblCircleMember.AddCircleMembers(cl);

            var cl2 = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m5, data = d1 }
            };
            db.tblCircleMember.AddCircleMembers(cl2);

            db.tblCircleMember.DeleteMembersFromAllCircles(new List<Guid>() { m1, m2 });

            var r = db.tblCircleMember.GetCircleMembers(c1);
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);

            r = db.tblCircleMember.GetCircleMembers(c2);
            Debug.Assert(r.Count == 3);
        }

        [Test]
        public void GetMembersCirclesAndDataTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

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

            var cl = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberItem() { circleId = c1, memberId = m2, data = d2 },
                new CircleMemberItem() { circleId = c1, memberId = m3, data = d3 } };

            db.tblCircleMember.AddCircleMembers(cl);

            var cl2 = new List<CircleMemberItem> {
                new CircleMemberItem() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberItem() { circleId = c2, memberId = m3, data = d2 },
                new CircleMemberItem() { circleId = c2, memberId = m4, data = d3 },
                new CircleMemberItem() { circleId = c2, memberId = m5, data = null }
            };
            db.tblCircleMember.AddCircleMembers(cl2);

            var r = db.tblCircleMember.GetMemberCirclesAndData(m1);
            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);

            r = db.tblCircleMember.GetMemberCirclesAndData(m2);
            Debug.Assert(r.Count == 2);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].data, d1) == 0) || (ByteArrayUtil.muidcmp(r[0].data, d2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1].data, d1) == 0) || (ByteArrayUtil.muidcmp(r[1].data, d2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].data, r[1].data) != 0));
        }
    }
}
