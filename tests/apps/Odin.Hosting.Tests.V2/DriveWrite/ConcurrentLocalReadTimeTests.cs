#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
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
    public async Task TwoConcurrentCallsWithTimestamps_BothSucceed_AndTheLaterTimeIsKept()
    {
        const int rounds = 8;
        var (tenant, context, file) = await SeedFileAsync();
        var earlier = UnixTimeUtc.Now().AddSeconds(-60);

        for (var round = 0; round < rounds; round++)
        {
            var a = earlier.AddMilliseconds(round * 2);
            var b = a.AddSeconds(30);
            await RunConcurrentlyAsync(tenant, context, file, a, b);
            Assert.That(await ReadTimeAsync(tenant, context, file), Is.EqualTo(b), $"round {round}: the later read time must win");
        }
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

    private static async Task<UnixTimeUtc?> ReadTimeAsync(ILifetimeScope tenant, IOdinContext context, InternalDriveFileId file)
    {
        await using var scope = tenant.BeginLifetimeScope();
        var header = await scope.Resolve<StandardFileSystem>().Storage.GetServerFileHeader(file, context);
        return header.FileMetadata.LocalAppData?.ReadTime;
    }
}
