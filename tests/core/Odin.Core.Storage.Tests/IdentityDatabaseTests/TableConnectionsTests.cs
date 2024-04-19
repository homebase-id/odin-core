﻿using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableConnectionsTests
    {
        [Test]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var g1 = Guid.NewGuid();
                var g2 = Guid.NewGuid();
                var g3 = Guid.NewGuid();

                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item1);

                var item2 = new ConnectionsRecord()
                {
                    identity = new OdinId("samwise.gamgee.me"),
                    displayName = "Sam",
                    status = 43,
                    accessIsRevoked = 0,
                    data = g2.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item2);

                var item3 = new ConnectionsRecord()
                {
                    identity = new OdinId("gandalf.white.me"),
                    displayName = "G",
                    status = 44,
                    accessIsRevoked = 0,
                    data = g3.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item3);

                // We have three connections, get the first two in the first page, then the last page of one
                //
                var r = db.tblConnections.PagingByIdentity(myc, 2, null, out var outCursor);
                Debug.Assert(r.Count == 2);

                r = db.tblConnections.PagingByIdentity(myc, 2, outCursor, out outCursor);
                Debug.Assert(r.Count == 1, message: "rdr.HasRows is the sinner");
                Debug.Assert(outCursor == null);


                // Try the filter ones
                r = db.tblConnections.PagingByIdentity(myc, 2, 42, null, out outCursor);
                Debug.Assert(r.Count == 1);
            }
        }


        [Test]
        public void InsertValidConnectionTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var g1 = Guid.NewGuid();

                // This is OK {odin.vahalla.com, driveid}
                var item = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo Baggins",
                    status = 42,
                    accessIsRevoked = 0,
                    data = g1.ToByteArray()
                };

                db.tblConnections.Upsert(myc, item);

                var r = db.tblConnections.Get(myc, new OdinId("frodo.baggins.me"));
                Debug.Assert(r.identity == "frodo.baggins.me");
                Debug.Assert(r.displayName == "Frodo Baggins");
                Debug.Assert(r.status == 42);
                Debug.Assert(r.accessIsRevoked == 0);
                Debug.Assert((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));
            }
        }


        [Test]
        public void DeleteValidConnectionTest()
        {
            using var db = new IdentityDatabase("");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var g1 = Guid.NewGuid();

                // This is OK {odin.vahalla.com, driveid}
                var item = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };

                var r = db.tblConnections.Get(myc, new OdinId("frodo.baggins.me"));
                Debug.Assert(r == null);
                db.tblConnections.Upsert(myc, item);
                r = db.tblConnections.Get(myc, new OdinId("frodo.baggins.me"));
                Debug.Assert(r.identity == "frodo.baggins.me");
                Debug.Assert(r.displayName == "");
                Debug.Assert(r.status == 42);
                Debug.Assert(r.accessIsRevoked == 1);
                Debug.Assert((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));
            }
        }


        [Test]
        public void PagingByCreatedBothTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var g1 = Guid.NewGuid();
                var g2 = Guid.NewGuid();
                var g3 = Guid.NewGuid();

                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item1);

                var item2 = new ConnectionsRecord()
                {
                    identity = new OdinId("samwise.gamgee.me"),
                    displayName = "Sam",
                    status = 43,
                    accessIsRevoked = 0,
                    data = g2.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item2);

                var item3 = new ConnectionsRecord()
                {
                    identity = new OdinId("gandalf.white.me"),
                    displayName = "G",
                    status = 42,
                    accessIsRevoked = 0,
                    data = g3.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item3);


                // Test the CRUD 

                // Get most recent (will be a different order)
                var r = db.tblConnections.PagingByCreated(myc, 2, null, out var timeCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(r[1].identity == "samwise.gamgee.me");
                Debug.Assert(timeCursor != null);
                r = db.tblConnections.PagingByCreated(myc, 2, timeCursor, out timeCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(timeCursor == null);


                // TEST THE HANDCODED
                r = db.tblConnections.PagingByCreated(myc, 1, 42, null, out timeCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(timeCursor != null);
                r = db.tblConnections.PagingByCreated(myc, 2, 42, timeCursor, out timeCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(timeCursor == null);
            }
        }


        [Test]
        public void GetConnectionsValidConnectionsTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var g1 = Guid.NewGuid();
                var g2 = Guid.NewGuid();
                var g3 = Guid.NewGuid();

                var item1 = new ConnectionsRecord()
                {
                    identity = new OdinId("frodo.baggins.me"),
                    displayName = "Frodo",
                    status = 42,
                    accessIsRevoked = 1,
                    data = g1.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item1);

                var item2 = new ConnectionsRecord()
                {
                    identity = new OdinId("samwise.gamgee.me"),
                    displayName = "Sam",
                    status = 43,
                    accessIsRevoked = 0,
                    data = g2.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item2);

                var item3 = new ConnectionsRecord()
                {
                    identity = new OdinId("gandalf.white.me"),
                    displayName = "G",
                    status = 42,
                    accessIsRevoked = 0,
                    data = g3.ToByteArray()
                };
                db.tblConnections.Upsert(myc, item3);


                var r = db.tblConnections.PagingByIdentity(myc, 2, null, out var outCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(r[1].identity == "gandalf.white.me");

                r = db.tblConnections.PagingByIdentity(myc, 2, outCursor, out outCursor);
                Debug.Assert(r.Count == 1, message: "rdr.HasRows is the sinner");
                Debug.Assert(r[0].identity == "samwise.gamgee.me");
                Debug.Assert(outCursor == null);

                // TEST HAND CODED STATUS FILTER
                r = db.tblConnections.PagingByIdentity(myc, 1, 42, null, out outCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(outCursor != null);
                r = db.tblConnections.PagingByIdentity(myc, 1, 42, outCursor, out outCursor);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(outCursor == null);



                // Get most recent (will be a different order)
                r = db.tblConnections.PagingByCreated(myc, 2, null, out var timeCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(r[1].identity == "samwise.gamgee.me");
                Debug.Assert(timeCursor != null);

                // TEST THE HANDCODED
                r = db.tblConnections.PagingByCreated(myc, 2, 43, null, out timeCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "samwise.gamgee.me");
                Debug.Assert(timeCursor == null);


                // PagingByCreated is NOT designed to be used with anything except the first page.
                // Hollow if you need pages and pages of 'most recent'. Hopefully just getting the
                // N you need is enough.
            }
        }
    }
}