using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Registry.PayloadMove;

#nullable enable

namespace Odin.Services.Tests.Registry.PayloadMove;

/// <summary>
/// The target side of a payload move against a scripted source and a real disk store.
/// </summary>
public class PayloadMoveTransferTests
{
    private const long StartRowId = 30;

    private string _root = null!;
    private DiskFileStore _target = null!;
    private List<FilePayloadRow> _index = null!;
    private FakeSource _source = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "payload-move-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = _root,
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
                FileWriteChunkSizeInBytes = 4096,
            }
        };
        _target = new DiskFileStore(new FileReaderWriter(config, new Mock<ILogger<FileReaderWriter>>().Object));

        // Three files the import brought (rowIds 10, 20, 30), each a payload with one thumbnail, and one made on
        // the target after the import (40), whose payloads were never on the source
        _index = [File(10), File(20), File(30), File(40)];
        _source = new FakeSource();
        Seed(ImportedObjects());
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public async Task MovesEveryObjectNewestFirstAndReleasesTheSource()
    {
        var state = NewState();

        var result = await Transfer(parallelism: 1).RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(result.End, Is.EqualTo(SliceEnd.Finished));
        Assert.That(state.Status, Is.EqualTo(PayloadMoveStatus.Complete), string.Join("; ", state.Failures));
        Assert.That(_source.CompleteCalls, Is.EqualTo(1));
        Assert.That(state.Credential, Is.EqualTo("credential"));
        Assert.That(state.HandoffToken, Is.Null, "the token is spent");

        var moved = ImportedObjects().ToList();
        foreach (var o in moved)
        {
            Assert.That(await _target.ReadAllBytesAsync(PathOf(o)), Is.EqualTo(Bytes(o)), o.ToString());
        }

        Assert.That(state.Objects, Is.EqualTo(6));
        Assert.That(state.Files, Is.EqualTo(3));
        Assert.That(_source.Fetched.Select(p => p.FileId).Distinct(),
            Is.EqualTo(new[] { FileIdOf(30), FileIdOf(20), FileIdOf(10) }), "newest first");
        Assert.That(_source.Fetched.Any(p => p.FileId == FileIdOf(40)), Is.False, "a file made after the import is not the source's");
    }

    [Test]
    public async Task NeverHasMoreThanNTransfersInFlight()
    {
        _index = Enumerable.Range(1, 10).Select(i => File(i)).ToList();
        Seed(_index.SelectMany(ObjectsOf));

        _source.Delay = TimeSpan.FromMilliseconds(30);
        var state = NewState(startRowId: 10);

        await Transfer(parallelism: 3).RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(state.Status, Is.EqualTo(PayloadMoveStatus.Complete), string.Join("; ", state.Failures));
        Assert.That(_source.MaxInFlight, Is.EqualTo(3), "at most, and actually using, the parallelism");
    }

    [Test]
    public async Task AThrottledSourceIsWaitedOutWithoutAdvancingOrFailing()
    {
        _source.ThrottleNextFetches = 1;
        _source.RetryAfter = TimeSpan.FromSeconds(42);
        var state = NewState();

        var first = await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(first.End, Is.EqualTo(SliceEnd.Wait));
        Assert.That(first.Wait, Is.EqualTo(TimeSpan.FromSeconds(42)), "the source's Retry-After wins");
        Assert.That(state.CursorRowId, Is.EqualTo(StartRowId + 1), "the cursor stays");
        Assert.That(state.FailureCount, Is.EqualTo(0), "throttled is not failed");

        var second = await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(second.End, Is.EqualTo(SliceEnd.Finished));
        Assert.That(state.Status, Is.EqualTo(PayloadMoveStatus.Complete), string.Join("; ", state.Failures));
        Assert.That(state.Objects + state.Skipped, Is.EqualTo(6));
    }

    [Test]
    public async Task ResumesAfterAStopWithoutFetchingAnythingTwice()
    {
        var state = NewState();
        using var stop = new CancellationTokenSource();
        _source.AfterFetch = count =>
        {
            if (count == 3)
            {
                stop.Cancel();
            }
        };

        Assert.CatchAsync<OperationCanceledException>(() =>
            Transfer(parallelism: 1).RunSliceAsync(state, TimeSpan.FromMinutes(1), stop.Token));
        Assert.That(state.CursorRowId, Is.EqualTo(StartRowId + 1), "the stopped batch is not committed");

        _source.AfterFetch = null;
        var fetchedBeforeTheStop = _source.Fetched.Count;
        var all = ImportedObjects().ToList();
        var landed = 0;
        foreach (var o in all)
        {
            landed += await _target.ExistsAsync(PathOf(o)) ? 1 : 0;
        }
        Assert.That(landed, Is.InRange(1, 5), "sanity: the stop came part way through");

        var resumed = await Transfer(parallelism: 1).RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(resumed.End, Is.EqualTo(SliceEnd.Finished));
        Assert.That(state.Status, Is.EqualTo(PayloadMoveStatus.Complete), string.Join("; ", state.Failures));
        Assert.That(state.Skipped, Is.EqualTo(landed), "what landed before the stop is skipped");
        Assert.That(_source.Fetched.Count - fetchedBeforeTheStop, Is.EqualTo(all.Count - landed),
            $"only what had not landed is fetched again ({fetchedBeforeTheStop} fetched before the stop, {landed} landed)");
    }

    [Test]
    public async Task AMissingOrWrongSizedObjectIsAFailureThatKeepsTheSource()
    {
        var objects = ImportedObjects().ToList();
        _source.Objects.Remove(objects[0].SourcePath(Guid.Empty), out _);
        _source.Objects[objects[1].SourcePath(Guid.Empty)] = [1, 2, 3];
        var state = NewState();

        var result = await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(result.End, Is.EqualTo(SliceEnd.Finished));
        Assert.That(state.Status, Is.EqualTo(PayloadMoveStatus.CompleteWithFailures));
        Assert.That(state.FailureCount, Is.EqualTo(2), string.Join("; ", state.Failures));
        Assert.That(state.Failures, Has.Some.Contains("does not have it").And.Some.Contains("the header records"));
        Assert.That(state.Objects, Is.EqualTo(4), "the rest still moved");
        Assert.That(_source.CompleteCalls, Is.EqualTo(0), "the source is not released");
    }

    [Test]
    public async Task SkipsWhatTheTargetAlreadyHas()
    {
        var already = ObjectsOf(_index.Single(f => f.RowId == 20)).First();
        await _target.EnsureDirectoryAsync(Path.GetDirectoryName(PathOf(already))!);
        await _target.WriteBytesAsync(PathOf(already), Bytes(already));
        var state = NewState();

        await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(state.Skipped, Is.EqualTo(1));
        Assert.That(state.Objects, Is.EqualTo(5));
        Assert.That(_source.Fetched, Does.Not.Contain(already));
    }

    [Test]
    public async Task ARefusedHandoffTokenEndsTheMoveLoudly()
    {
        _source.RefuseRedeem = true;
        var state = NewState();

        var result = await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(result.End, Is.EqualTo(SliceEnd.Finished));
        Assert.That(state.Status, Is.EqualTo(PayloadMoveStatus.Refused));
        Assert.That(state.Failures.Single(), Does.Contain("already redeemed"));
        Assert.That(_source.Fetched, Is.Empty);
    }

    [Test]
    public async Task AnUnreachableSourceIsRetriedWithABackoffThatGrows()
    {
        _source.UnavailableRedeem = true;
        var state = NewState();

        var first = await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await Transfer().RunSliceAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(first.Wait, Is.EqualTo(PayloadMoveTransfer.FirstBackoff));
        Assert.That(second.Wait, Is.EqualTo(PayloadMoveTransfer.FirstBackoff * 2));
        Assert.That(state.HandoffToken, Is.EqualTo("token"), "not spent until redeemed");
    }

    //

    // What the source has: every object of the files the import brought
    private IEnumerable<PayloadObject> ImportedObjects() => _index.Where(f => f.RowId <= StartRowId).SelectMany(ObjectsOf);

    private void Seed(IEnumerable<PayloadObject> objects)
    {
        foreach (var o in objects)
        {
            _source.Objects[o.SourcePath(Guid.Empty)] = Bytes(o);
        }
    }

    private PayloadMoveTransfer Transfer(int parallelism = 5) => new(
        _source,
        _target,
        (below, count) => Task.FromResult(_index.Where(f => f.RowId < below).OrderByDescending(f => f.RowId).Take(count).ToList()),
        PathOf,
        parallelism,
        NullLogger.Instance);

    private static PayloadMoveState NewState(long startRowId = StartRowId)
    {
        var state = new PayloadMoveState { BaseUrl = "https://source.example", HandoffToken = "token" };
        state.StartFrom(startRowId);
        return state;
    }

    private string PathOf(PayloadObject o) => Path.Combine(_root, "payloads",
        o.IsThumbnail ? $"{o.FileId:N}-{o.Key}-{o.Uid.uniqueTime}-{o.Width}x{o.Height}.thumb" : $"{o.FileId:N}-{o.Key}-{o.Uid.uniqueTime}.payload");

    private static Guid FileIdOf(long rowId) => new($"00000000-0000-0000-0000-{rowId:D12}");

    private static byte[] Bytes(PayloadObject o) =>
        System.Text.Encoding.UTF8.GetBytes(o.ToString().PadRight((int)o.ExpectedLength, '.'))[..(int)o.ExpectedLength];

    private static FilePayloadRow File(long rowId)
    {
        var metadata = new FileMetadata
        {
            Payloads =
            [
                new PayloadDescriptor
                {
                    Key = "pay_key1",
                    Uid = new UnixTimeUtcUnique(1_000_000 + rowId),
                    BytesWritten = 300,
                    Thumbnails = [new ThumbnailDescriptor { PixelWidth = 20, PixelHeight = 10, BytesWritten = 150 }]
                }
            ]
        };
        return new FilePayloadRow(rowId, Guid.Parse("11111111-2222-3333-4444-555555555555"), FileIdOf(rowId),
            OdinSystemSerializer.Serialize(metadata));
    }

    private static IEnumerable<PayloadObject> ObjectsOf(FilePayloadRow file)
    {
        var payload = OdinSystemSerializer.Deserialize<FileMetadata>(file.FileMetaData)!.Payloads.Single();
        yield return new PayloadObject(file.DriveId, file.FileId, payload.Key, payload.Uid, payload.BytesWritten);
        var thumbnail = payload.Thumbnails.Single();
        yield return new PayloadObject(file.DriveId, file.FileId, payload.Key, payload.Uid, thumbnail.BytesWritten,
            thumbnail.PixelWidth, thumbnail.PixelHeight);
    }

    private sealed class FakeSource : IPayloadMoveSourceClient
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public List<PayloadObject> Fetched { get; } = [];
        public int CompleteCalls { get; private set; }
        public int MaxInFlight { get; private set; }
        public TimeSpan Delay { get; set; }
        public int ThrottleNextFetches { get; set; }
        public TimeSpan? RetryAfter { get; set; }
        public bool RefuseRedeem { get; set; }
        public bool UnavailableRedeem { get; set; }
        public Action<int>? AfterFetch { get; set; }

        private int _inFlight;
        private readonly object _lock = new();

        public Task<(FetchOutcome outcome, string? credential)> RedeemAsync(string handoffToken, CancellationToken cancellationToken)
        {
            if (UnavailableRedeem)
            {
                return Task.FromResult<(FetchOutcome, string?)>((new FetchOutcome(FetchResult.Unavailable, Error: "down"), null));
            }

            return Task.FromResult<(FetchOutcome, string?)>(RefuseRedeem || handoffToken != "token"
                ? (new FetchOutcome(FetchResult.NotFound), null)
                : (new FetchOutcome(FetchResult.Fetched), "credential"));
        }

        public async Task<FetchOutcome> FetchAsync(PayloadObject payloadObject, string credential, Stream destination,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (ThrottleNextFetches > 0)
                {
                    ThrottleNextFetches--;
                    return new FetchOutcome(FetchResult.Throttled, RetryAfter);
                }

                _inFlight++;
                MaxInFlight = Math.Max(MaxInFlight, _inFlight);
            }

            try
            {
                await Task.Delay(Delay, cancellationToken);
                if (credential != "credential" || !Objects.TryGetValue(payloadObject.SourcePath(Guid.Empty), out var bytes))
                {
                    return new FetchOutcome(FetchResult.NotFound);
                }

                await destination.WriteAsync(bytes, cancellationToken);
                int count;
                lock (_lock)
                {
                    Fetched.Add(payloadObject);
                    count = Fetched.Count;
                }

                AfterFetch?.Invoke(count);
                return new FetchOutcome(FetchResult.Fetched);
            }
            finally
            {
                lock (_lock)
                {
                    _inFlight--;
                }
            }
        }

        public Task<FetchOutcome> CompleteAsync(string credential, CancellationToken cancellationToken)
        {
            CompleteCalls++;
            return Task.FromResult(new FetchOutcome(credential == "credential" ? FetchResult.Fetched : FetchResult.NotFound));
        }
    }
}
