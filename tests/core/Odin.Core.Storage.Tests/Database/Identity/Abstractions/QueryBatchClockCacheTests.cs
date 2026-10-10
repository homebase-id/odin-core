using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

/// <summary>
/// #1905: a change-feed query (AnyChangeDate / OnlyModifiedDate) hides rows with modified at or after the database's
/// now, so its answer depends on the clock as well as the data. Such an answer must not be cached, or an identical
/// query keeps getting it until the next write to the drive.
///
/// Hitting the same millisecond as a write is not deterministic, so these tests move a row's modified slightly into
/// the future instead -- the state the query sees during that millisecond, and one that
/// modified = MAX(modified + 1, now) on rapid updates also produces. The rows are written through the uncached
/// MainIndexMeta, which does not clear the query cache, so every answer comes from the first query that caches it.
/// </summary>
public class QueryBatchClockCacheTests : IocTestBase
{
    private const int FutureMs = 300;
    private readonly IntRange _allIntRange = new(start: 0, end: 1000);

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, false)]
#endif
#if RUN_REDIS_TESTS
    [TestCase(DatabaseType.Sqlite, true)]
#endif
    public async Task OldestFirstShowsARowOnceTheClockPassesIt(DatabaseType databaseType, bool redisEnabled)
    {
        await RegisterServicesAsync(databaseType, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();
        var queryBatch = scope.Resolve<QueryBatch>();
        var queryBatchCached = scope.Resolve<QueryBatchCached>();
        var driveId = Guid.NewGuid();

        await AddAsync(scope, driveId);
        await Task.Delay(5);
        var (_, _, atEnd) = await queryBatch.QueryBatchAsync(driveId, 10, null, QueryBatchSortOrder.OldestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);

        var b = await AddAsync(scope, driveId);
        var visibleAt = await MoveModifiedIntoTheFutureAsync(scope, driveId, b);

        var before = await queryBatchCached.QueryBatchAsync(driveId, 10, atEnd, QueryBatchSortOrder.OldestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(before.Records), Is.Empty, "b is not visible before the clock reaches its modified");

        await WaitUntilPastAsync(visibleAt);

        var after = await queryBatchCached.QueryBatchAsync(driveId, 10, atEnd, QueryBatchSortOrder.OldestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(after.Records), Is.EqualTo(new[] { b }),
            "the identical query must see b once the clock has passed it");
        Assert.That(before.MoreRows, Is.True, "a row was held back, so the earlier answer must tell the client to come back");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task OnlyModifiedShowsARowOnceTheClockPassesIt(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var queryBatchCached = scope.Resolve<QueryBatchCached>();
        var driveId = Guid.NewGuid();

        // Moving modified away from created is also what makes it count as modified
        var b = await AddAsync(scope, driveId);
        var visibleAt = await MoveModifiedIntoTheFutureAsync(scope, driveId, b);

        var before = await queryBatchCached.QueryModifiedAsync(driveId, 10, null, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(before.Records), Is.Empty, "b is not visible before the clock reaches its modified");

        await WaitUntilPastAsync(visibleAt);

        var after = await queryBatchCached.QueryModifiedAsync(driveId, 10, null, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(after.Records), Is.EqualTo(new[] { b }), "the identical query must see b once the clock has passed it");
        Assert.That(before.MoreRows, Is.True, "a row was held back, so the earlier answer must tell the client to come back");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task NewestFirstShowsARowOnceTheClockPassesIt(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var queryBatchCached = scope.Resolve<QueryBatchCached>();
        var driveId = Guid.NewGuid();

        var a = await AddAsync(scope, driveId);
        await Task.Delay(5);
        var b = await AddAsync(scope, driveId);
        var visibleAt = await MoveModifiedIntoTheFutureAsync(scope, driveId, b);

        var before = await queryBatchCached.QueryBatchSmartCursorAsync(driveId, 10, null, QueryBatchSortOrder.NewestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(before.Records), Is.EqualTo(new[] { a }), "b is not visible before the clock reaches its modified");

        await WaitUntilPastAsync(visibleAt);

        var after = await queryBatchCached.QueryBatchSmartCursorAsync(driveId, 10, null, QueryBatchSortOrder.NewestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(after.Records), Is.EqualTo(new[] { b, a }), "the identical query must see b once the clock has passed it");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task AnAnswerThatHeldNothingBackIsStillCached(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var queryBatchCached = scope.Resolve<QueryBatchCached>();
        var driveId = Guid.NewGuid();

        var a = await AddAsync(scope, driveId);
        await Task.Delay(5);

        var first = await queryBatchCached.QueryBatchAsync(driveId, 10, null, QueryBatchSortOrder.OldestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(first.Records), Is.EqualTo(new[] { a }));
        Assert.That(first.MoreRows, Is.False, "nothing was held back and there are no more rows");

        // Written behind the cache's back: only a cached answer can still leave it out
        await AddAsync(scope, driveId);
        await Task.Delay(5);

        var second = await queryBatchCached.QueryBatchAsync(driveId, 10, null, QueryBatchSortOrder.OldestFirst,
            QueryBatchSortField.AnyChangeDate, requiredSecurityGroup: _allIntRange);
        Assert.That(Ids(second.Records), Is.EqualTo(new[] { a }), "an answer that held nothing back must still be cached");
    }

    //
    // Helpers
    //

    private static async Task<Guid> AddAsync(ILifetimeScope scope, Guid driveId)
    {
        var fileId = SequentialGuid.CreateGuid();
        await scope.Resolve<MainIndexMeta>().TestAddEntryPassalongToUpsertAsync(driveId, fileId, Guid.NewGuid(), 1, 1,
            "sender", null, null, 0, userDate: new UnixTimeUtc(0), requiredSecurityGroup: 1, accessControlList: null,
            tagIdList: null, byteCount: 1, fileState: 1);
        return fileId;
    }

    /// <summary>
    /// Sets the row's modified a little ahead of now and returns that time
    /// </summary>
    private static async Task<UnixTimeUtc> MoveModifiedIntoTheFutureAsync(ILifetimeScope scope, Guid driveId, Guid fileId)
    {
        var future = new UnixTimeUtc(UnixTimeUtc.Now().milliseconds + FutureMs);
        var updated = await scope.Resolve<TableDriveMainIndex>().TestSetModifiedAsync(driveId, fileId, future);
        Assert.That(updated, Is.EqualTo(1), "rows updated");
        return future;
    }

    private static async Task WaitUntilPastAsync(UnixTimeUtc time)
    {
        while (UnixTimeUtc.Now().milliseconds <= time.milliseconds + 5)
        {
            await Task.Delay(10);
        }
    }

    private static List<Guid> Ids(List<DriveMainIndexRecord> records) => records.Select(r => r.fileId).ToList();
}
