#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// Concurrent read-time updates for one file (#1724; see <c>DriveStorageServiceBase.UpdateLocalReadTime</c>): no call
/// fails, and the latest read time is kept.
/// </summary>
[TestFixture]
public class ConcurrentLocalReadTimeTests : V2Fixture
{
    [Test]
    public async Task ConcurrentCallsWithoutATimestamp_AllSucceed_AndOneWrites()
    {
        var (tenant, context, file) = await SeedFileAsync();

        var results = await RunConcurrentlyAsync(tenant, context, file, new UnixTimeUtc?[8]);

        Assert.That(results.Count(written => written), Is.EqualTo(1), "one call marks the file read; the rest find it read");
        Assert.That(await ReadTimeAsync(tenant, context, file), Is.Not.Null);
    }

    [Test]
    public async Task ManyConcurrentCallsWithTimestamps_AllSucceed_AndTheLatestTimeIsKept()
    {
        var (tenant, context, file) = await SeedFileAsync();
        var start = UnixTimeUtc.Now().AddSeconds(-3600);
        var timestamps = Enumerable.Range(0, 8).Select(i => (UnixTimeUtc?)start.AddSeconds(i * 10)).ToArray();

        await RunConcurrentlyAsync(tenant, context, file, timestamps);

        Assert.That(await ReadTimeAsync(tenant, context, file), Is.EqualTo(timestamps[^1]), "the latest read time must win");
    }

    [Test]
    public async Task TwoConcurrentCallsWithTimestamps_BothSucceed_AndTheLaterTimeIsKept()
    {
        const int rounds = 8;
        var (tenant, context, file) = await SeedFileAsync();
        var start = UnixTimeUtc.Now().AddSeconds(-3600);

        for (var round = 0; round < rounds; round++)
        {
            // Both past the round before's, so both may write and every round races; both in the past, so neither is clamped
            var a = start.AddSeconds(round * 60);
            var b = a.AddSeconds(30);
            await RunConcurrentlyAsync(tenant, context, file, a, b);
            Assert.That(await ReadTimeAsync(tenant, context, file), Is.EqualTo(b), $"round {round}: the later read time must win");
        }
    }

    /// <summary>
    /// The window a racing loser retries in: the winner has committed, but drops the cached header only in a post-commit
    /// action, so the cache still holds the local version the loser lost on.  The loser runs in a post-commit action
    /// queued ahead of the winner's, so the whole call is in that window: a retry read from the cache loses on every round.
    /// </summary>
    [Test]
    public async Task ALoserRetryingBeforeTheWinnersCacheEviction_ReadsPastTheCache_AndWrites()
    {
        var (tenant, context, file) = await SeedFileAsync();
        var start = UnixTimeUtc.Now().AddSeconds(-3600);
        var winnerTime = start;
        var loserTime = start.AddSeconds(30);

        // The cache holds the header with no read time
        await using (var primeScope = tenant.BeginLifetimeScope())
        {
            Assert.That((await primeScope.Resolve<StandardFileSystem>().Storage.GetServerFileHeaderForWriting(file, context))
                .FileMetadata.LocalAppData?.ReadTime, Is.Null);
        }

        bool? loserWrote = null;
        Exception? loserFailure = null;
        await using (var winnerScope = tenant.BeginLifetimeScope())
        {
            await using var tx = await winnerScope.Resolve<IdentityDatabase>().BeginStackedTransactionAsync();
            tx.AddPostCommitAction(async () =>
            {
                await using var loserScope = tenant.BeginLifetimeScope();
                try
                {
                    loserWrote = await loserScope.Resolve<StandardFileSystem>().Storage.UpdateLocalReadTime(file, context, loserTime);
                }
                catch (Exception e)
                {
                    loserFailure = e;
                }
            });

            Assert.That(await winnerScope.Resolve<StandardFileSystem>().Storage.UpdateLocalReadTime(file, context, winnerTime), Is.True);
            tx.Commit();
        }

        Assert.That(loserFailure, Is.Null, "the loser's retry must read the winner's header, not the cached one");
        Assert.That(loserWrote, Is.True, "the loser's later read time is behind no one's: it writes");
        Assert.That(await ReadTimeAsync(tenant, context, file), Is.EqualTo(loserTime));
    }

    private async Task<(ILifetimeScope Tenant, IOdinContext Context, InternalDriveFileId File)> SeedFileAsync()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var seeded = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "read me");
        var (tenant, context) = await MigrationContextAsync(owner);
        return (tenant, context, new InternalDriveFileId(spec.TargetDrive.Alias, seeded.FileId));
    }

    /// <summary>
    /// One call per timestamp, all released at once, each in its own scope as concurrent requests are: a scope's
    /// connection is not for parallel use.
    /// </summary>
    private static async Task<bool[]> RunConcurrentlyAsync(ILifetimeScope tenant, IOdinContext context, InternalDriveFileId file,
        params UnixTimeUtc?[] timestamps)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = timestamps.Select(async timestamp =>
        {
            await using var scope = tenant.BeginLifetimeScope();
            var fs = scope.Resolve<StandardFileSystem>();
            await start.Task;
            return await fs.Storage.UpdateLocalReadTime(file, context, timestamp);
        }).ToArray();

        start.SetResult();
        return await Task.WhenAll(calls);
    }

    /// <summary>The stored read time, read in a transaction so it comes from the database, not the cache.</summary>
    private static async Task<UnixTimeUtc?> ReadTimeAsync(ILifetimeScope tenant, IOdinContext context, InternalDriveFileId file)
    {
        await using var scope = tenant.BeginLifetimeScope();
        await using var tx = await scope.Resolve<IdentityDatabase>().BeginStackedTransactionAsync();
        var header = await scope.Resolve<StandardFileSystem>().Storage.GetServerFileHeader(file, context);
        return header.FileMetadata.LocalAppData?.ReadTime;
    }
}
