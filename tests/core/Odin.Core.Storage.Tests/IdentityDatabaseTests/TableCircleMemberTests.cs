using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableCircleMemberTests
    {
        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests001");
            db.CreateDatabase();

            var c1 = Guid.NewGuid();
            var m1 = Guid.NewGuid();
            var d1 = Guid.NewGuid().ToByteArray();

            var cl = new List<CircleMemberRecord> { new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 } };
            db.tblCircleMember.UpsertCircleMembers(cl);

            var r = db.tblCircleMember.GetCircleMembers(c1);

            Debug.Assert(r.Count == 1);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m1) == 0);
        }


        [Test]
        public void InsertDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests002");

            var c1 = Guid.NewGuid();
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var m1 = Guid.NewGuid();

                var cl = new List<CircleMemberRecord> { new CircleMemberRecord() { circleId = c1, memberId = m1, data = null } };
                db.tblCircleMember.UpsertCircleMembers(cl);

                bool ok = false;
                try
                {
                    db.tblCircleMember.UpsertCircleMembers(cl);
                }
                catch
                {
                    ok = true;
                }

                //Note: this now runs upsert so there should be no error
                Assert.IsFalse(ok);
            }
        }


        [Test]
        public void InsertEmptyTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var c1 = Guid.NewGuid();
                var m1 = Guid.NewGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var cl = new List<CircleMemberRecord> { new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 } };

                bool ok = false;
                try
                {
                    db.tblCircleMember.UpsertCircleMembers(new List<CircleMemberRecord>());
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        public void InsertMultipleMembersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var c1 = Guid.NewGuid();
                var m1 = Guid.NewGuid();
                var d1 = Guid.NewGuid().ToByteArray();

                var m2 = Guid.NewGuid();
                var m3 = Guid.NewGuid();

                var cl = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberRecord() { circleId = c1, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c1, memberId = m3, data = d1 } };

                db.tblCircleMember.UpsertCircleMembers(cl);

                var r = db.tblCircleMember.GetCircleMembers(c1);

                Debug.Assert(r.Count == 3);
            }
        }


        [Test]
        public void InsertMultipleCirclesTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
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

                db.tblCircleMember.UpsertCircleMembers(cl);

                var cl2 = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m5, data = d1 }
            };
                db.tblCircleMember.UpsertCircleMembers(cl2);

                var r = db.tblCircleMember.GetCircleMembers(c1);
                Debug.Assert(r.Count == 3);
                r = db.tblCircleMember.GetCircleMembers(c2);
                Debug.Assert(r.Count == 4);
            }
        }


        [Test]
        public void RemoveMembersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
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

                db.tblCircleMember.UpsertCircleMembers(cl);

                var cl2 = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m5, data = d1 }
            };
                db.tblCircleMember.UpsertCircleMembers(cl2);

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
        }


        [Test]
        public void DeleteMembersEmptyTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests007");

            using (var myc = db.CreateDisposableConnection())
            {
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
        }


        [Test]
        public void DeleteMembersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests008");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
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

                db.tblCircleMember.UpsertCircleMembers(cl);

                var cl2 = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m3, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m4, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m5, data = d1 }
            };
                db.tblCircleMember.UpsertCircleMembers(cl2);

                db.tblCircleMember.DeleteMembersFromAllCircles(new List<Guid>() { m1, m2 });

                var r = db.tblCircleMember.GetCircleMembers(c1);
                Debug.Assert(r.Count == 1);
                Debug.Assert(ByteArrayUtil.muidcmp(r[0].memberId, m3) == 0);

                r = db.tblCircleMember.GetCircleMembers(c2);
                Debug.Assert(r.Count == 3);
            }
        }

        [Test]
        public void GetMembersCirclesAndDataTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableCircleMemberTests009");

            using (var myc = db.CreateDisposableConnection())
            {
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

                var cl = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c1, memberId = m1, data = d1 },
                new CircleMemberRecord() { circleId = c1, memberId = m2, data = d2 },
                new CircleMemberRecord() { circleId = c1, memberId = m3, data = d3 } };

                db.tblCircleMember.UpsertCircleMembers(cl);

                var cl2 = new List<CircleMemberRecord> {
                new CircleMemberRecord() { circleId = c2, memberId = m2, data = d1 },
                new CircleMemberRecord() { circleId = c2, memberId = m3, data = d2 },
                new CircleMemberRecord() { circleId = c2, memberId = m4, data = d3 },
                new CircleMemberRecord() { circleId = c2, memberId = m5, data = null }
            };
                db.tblCircleMember.UpsertCircleMembers(cl2);

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
}
