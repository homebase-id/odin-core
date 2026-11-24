#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// Concurrent read-time updates for one file (#1724): a client that fires two read receipts at once used to lose one,
/// whose save named a local version the other had just replaced.  The loser now reads the file again and judges it
/// afresh, so no call fails and the latest read time is kept.
/// </summary>
[TestFixture]
public class ConcurrentLocalReadTimeTests : V2Fixture
{
    private const int Calls = 8;

    [Test]
    public async Task ConcurrentCallsWithoutATimestamp_AllSucceed_AndOneWrites()
    {
        var (owner, file) = await SeedFileAsync();

        var results = await RunConcurrentlyAsync(owner, file, _ => null);

        Assert.That(results.Count(written => written), Is.EqualTo(1), "one call marks the file read; the rest find it read");
        Assert.That(await ReadTimeAsync(owner, file), Is.Not.Null);
    }

    [Test]
    public async Task TwoConcurrentCallsWithTimestamps_BothSucceed_AndTheLaterTimeIsKept()
    {
        var (owner, file) = await SeedFileAsync();
        var earlier = UnixTimeUtc.Now().AddSeconds(-60);
        var later = earlier.AddSeconds(30);

        for (var round = 0; round < Calls; round++)
        {
            var (a, b) = (earlier.AddMilliseconds(round * 2), later.AddMilliseconds(round * 2));
            await RunConcurrentlyAsync(owner, file, i => i == 0 ? a : b, calls: 2);
            Assert.That(await ReadTimeAsync(owner, file), Is.EqualTo(b), $"round {round}: the later read time must win");
        }
    }

    private async Task<(OwnerSession Owner, InternalDriveFileId File)> SeedFileAsync()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var seeded = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "read me");
        return (owner, new InternalDriveFileId(spec.TargetDrive.Alias, seeded.FileId));
    }

    /// <summary>Each call in its own scope, as concurrent requests are: a scope's connection is not for parallel use.</summary>
    private async Task<bool[]> RunConcurrentlyAsync(OwnerSession owner, InternalDriveFileId file, Func<int, UnixTimeUtc?> timestamp,
        int calls = Calls)
    {
        var (tenant, context) = await MigrationContextAsync(owner);
        using var start = new System.Threading.ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, calls).Select(i => Task.Run(async () =>
        {
            await using var scope = tenant.BeginLifetimeScope();
            var fs = scope.Resolve<StandardFileSystem>();
            start.Wait();
            return await fs.Storage.UpdateLocalReadTime(file, context, timestamp(i));
        })).ToArray();

        start.Set();
        return await Task.WhenAll(tasks);
    }

    private async Task<UnixTimeUtc?> ReadTimeAsync(OwnerSession owner, InternalDriveFileId file)
    {
        var (tenant, context) = await MigrationContextAsync(owner);
        await using var scope = tenant.BeginLifetimeScope();
        var header = await scope.Resolve<StandardFileSystem>().Storage.GetServerFileHeader(file, context);
        return header.FileMetadata.LocalAppData?.ReadTime;
    }
}
