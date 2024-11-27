﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableCircleTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task InsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai1", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai2", circleId = c2, data = d2 });

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

            // Result set is ordered
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task DeleteCircleTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai1", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai2", circleId = c2, data = d2 });

            await tblCircle.DeleteAsync(c2);

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

            // Result set is ordered
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c2, data = d2 });

            var r = await tblCircle.GetAsync(c1);
            Debug.Assert(ByteArrayUtil.muidcmp(r.circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r.data, d1) == 0);

            r = await tblCircle.GetAsync(c2);
            Debug.Assert(ByteArrayUtil.muidcmp(r.circleId, c2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r.data, d2) == 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetAllCirclesEmptyTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            Debug.Assert(r.Count == 0);
            Debug.Assert(nextCursor == null);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task GetAllCirclesTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c2, data = d2 });

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
        }
    }
}