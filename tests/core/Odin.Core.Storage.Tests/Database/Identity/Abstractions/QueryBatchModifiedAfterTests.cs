using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions
{
    /// <summary>
    /// Coverage for the <c>modifiedAfter</c> clamp added to <see cref="QueryBatch.QueryBatchAsync"/>, which
    /// backs the temporal (time-boxed) read API. The clamp is an inclusive lower bound on the server-set
    /// <c>modified</c> column.
    /// </summary>
    public class QueryBatchModifiedAfterTests : IocTestBase
    {
        private readonly IntRange _allIntRange = new(start: 0, end: 1000);

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task ModifiedAfter_ClampsToInclusiveLowerBound(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var queryBatch = scope.Resolve<QueryBatch>();

            var driveId = Guid.NewGuid();
            var sender = SequentialGuid.CreateGuid().ToString();
            var tag = SequentialGuid.CreateGuid();

            // Insert three rows with distinct modified timestamps (delays guarantee distinct milliseconds).
            var f1 = SequentialGuid.CreateGuid();
            var (_, m1) = await AddAsync(metaIndex, driveId, f1, sender, tag);
            await Task.Delay(8);
            var f2 = SequentialGuid.CreateGuid();
            var (_, m2) = await AddAsync(metaIndex, driveId, f2, sender, tag);
            await Task.Delay(8);
            var f3 = SequentialGuid.CreateGuid();
            var (_, m3) = await AddAsync(metaIndex, driveId, f3, sender, tag);

            ClassicAssert.IsTrue(m1 < m2 && m2 < m3, "expected strictly increasing modified timestamps");

            // No clamp -> all three.
            var all = await QueryAsync(queryBatch, driveId, null);
            ClassicAssert.AreEqual(3, all.Count);

            // Clamp at m2 -> inclusive, so f2 and f3 only (f1 excluded).
            var fromM2 = await QueryAsync(queryBatch, driveId, m2);
            CollectionAssert.AreEquivalent(new[] { f2, f3 }, fromM2);

            // Clamp at m3 -> only f3.
            var fromM3 = await QueryAsync(queryBatch, driveId, m3);
            CollectionAssert.AreEquivalent(new[] { f3 }, fromM3);

            // Clamp just past m3 -> nothing.
            var pastM3 = await QueryAsync(queryBatch, driveId, new UnixTimeUtc(m3.milliseconds + 1));
            ClassicAssert.AreEqual(0, pastM3.Count);

            // Clamp at m1 -> all three (lower boundary inclusive).
            var fromM1 = await QueryAsync(queryBatch, driveId, m1);
            ClassicAssert.AreEqual(3, fromM1.Count);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task GetNewestModified_ReturnsMaxModified_OrZeroWhenEmpty(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();
            var tbl = scope.Resolve<TableDriveMainIndex>();

            var driveId = Guid.NewGuid();
            var fst = (int)FileSystemType.Standard;
            var sender = SequentialGuid.CreateGuid().ToString();
            var tag = SequentialGuid.CreateGuid();

            // Empty drive -> 0.
            ClassicAssert.AreEqual(0, await tbl.GetNewestModifiedAsync(driveId, fst));

            var (_, m1) = await AddAsync(metaIndex, driveId, SequentialGuid.CreateGuid(), sender, tag);
            await Task.Delay(8);
            var (_, m2) = await AddAsync(metaIndex, driveId, SequentialGuid.CreateGuid(), sender, tag);

            // Returns the newest modified.
            ClassicAssert.AreEqual(m2.milliseconds, await tbl.GetNewestModifiedAsync(driveId, fst));
            ClassicAssert.IsTrue(m2.milliseconds > m1.milliseconds);

            // Different file system type on this drive -> 0 (no Comment files inserted).
            ClassicAssert.AreEqual(0, await tbl.GetNewestModifiedAsync(driveId, (int)FileSystemType.Comment));

            // Different drive -> 0.
            ClassicAssert.AreEqual(0, await tbl.GetNewestModifiedAsync(Guid.NewGuid(), fst));
        }

        private async Task<(UnixTimeUtc created, UnixTimeUtc modified)> AddAsync(MainIndexMeta metaIndex, Guid driveId, Guid fileId,
            string sender, Guid tag)
        {
            return await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, fileId, Guid.NewGuid(), 1, 1, sender, tag, null, 42,
                userDate: new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: null, tagIdList: null, byteCount: 1,
                fileState: 1); // Active
        }

        private async Task<List<Guid>> QueryAsync(QueryBatch queryBatch, Guid driveId,
            UnixTimeUtc? modifiedAfter)
        {
            var (result, _, _) = await queryBatch.QueryBatchAsync(driveId, 100, cursor: null,
                requiredSecurityGroup: _allIntRange, modifiedAfter: modifiedAfter);
            return result.Select(r => r.fileId).ToList();
        }
    }
}
