using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveRead;

/// <summary>
/// Payload GETs stream from storage instead of loading the payload into memory (#1892). The stream cannot
/// seek, so the controller sets Content-Length itself; these pin it, and Content-Range, for whole, ranged and
/// open-ended reads of a payload larger than the storage read buffer, plus 64-bit Range values (#1893).
/// </summary>
[TestFixture]
public class PayloadStreamingReadTests : V2Fixture
{
    private const string PayloadKey = "bigpayld1";
    private const int PayloadSize = 5 * 1024 * 1024 + 17;

    [Test]
    public async Task WholePayloadStreamsWithContentLength()
    {
        var (owner, drive, file, bytes) = await UploadBigPayload();

        var response = await owner.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, PayloadKey);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ContentHeaders!.ContentLength, Is.EqualTo(bytes.Length));
        Assert.That(await response.Content!.ReadAsByteArrayAsync(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task RangeHeaderStreamsTheRange()
    {
        var (owner, drive, file, bytes) = await UploadBigPayload();
        const long from = 1024 * 1024 - 3, to = 3 * 1024 * 1024 + 5;

        using var response = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(from, to));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent), await Body(response));
        AssertContentRange(response, from, to, bytes.Length);
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(bytes[(int)from..((int)to + 1)]));
    }

    [Test]
    public async Task OpenEndedRangeHeaderStreamsToTheEnd()
    {
        var (owner, drive, file, bytes) = await UploadBigPayload();
        const long from = 2 * 1024 * 1024 + 1;

        using var response = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(from, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent), await Body(response));
        AssertContentRange(response, from, bytes.Length - 1, bytes.Length);
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(bytes[(int)from..]));
    }

    [Test]
    public async Task RangeRunningPastTheEndIsClamped()
    {
        var (owner, drive, file, bytes) = await UploadBigPayload();

        // RFC 9110 14.1.2: satisfiable, served up to the end
        using var response = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(10, bytes.Length + 10));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent), await Body(response));
        AssertContentRange(response, 10, bytes.Length - 1, bytes.Length);
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(bytes[10..]));
    }

    [Test]
    public async Task RangeStartingAtTheEndIsNotSatisfiable()
    {
        var (owner, drive, file, bytes) = await UploadBigPayload();

        using var response = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(bytes.Length, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestedRangeNotSatisfiable), await Body(response));
        Assert.That(response.Content.Headers.ContentRange?.ToString(), Is.EqualTo($"bytes */{bytes.Length}"));
    }

    [Test]
    public async Task RangeValuesPast2GiBAreParsedNotOverflowed()
    {
        var (owner, drive, file, _) = await UploadBigPayload();

        // Past Int32.MaxValue: Convert.ToInt32 used to throw (a 500). Both start past this payload's end.
        using var bounded = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(3000000000, 3000000099));
        Assert.That(bounded.StatusCode, Is.EqualTo(HttpStatusCode.RequestedRangeNotSatisfiable), await Body(bounded));

        using var openEnded = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(3000000000, null));
        Assert.That(openEnded.StatusCode, Is.EqualTo(HttpStatusCode.RequestedRangeNotSatisfiable), await Body(openEnded));
    }

    [Test]
    public async Task RangeFromZeroByUniqueIdIsARange()
    {
        // The by-uid and by-gtid routes used to read start 0 as "no range" and send the whole payload
        var (owner, _, file, bytes) = await UploadBigPayload();

        var response = await owner.Drives.Reader.GetPayloadByUniqueIdAsync(file.UniqueId, file.DriveId, PayloadKey,
            new Services.Drives.FileSystem.Base.FileChunk { Start = 0, Length = 100 });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        Assert.That(response.ContentHeaders!.ContentLength, Is.EqualTo(100));
        Assert.That(await response.Content!.ReadAsByteArrayAsync(), Is.EqualTo(bytes[..100]));
    }

    [Test]
    public async Task RangeEndingAtLongMaxValueIsToTheEnd()
    {
        var (owner, drive, file, bytes) = await UploadBigPayload();

        // To - From + 1 used to overflow to long.MinValue, a 400
        using var response = await GetV1WithRange(owner, drive, file, new RangeHeaderValue(0, long.MaxValue));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent), await Body(response));
        AssertContentRange(response, 0, bytes.Length - 1, bytes.Length);
    }

    [Test]
    public async Task V2RangeRouteLengthZeroIsToTheEnd()
    {
        var (owner, _, file, bytes) = await UploadBigPayload();
        const long start = 5 * 1024 * 1024 - 3;

        // A FileChunk without a Length goes on the route as length 0
        var response = await owner.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, PayloadKey,
            new Services.Drives.FileSystem.Base.FileChunk { Start = start });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        Assert.That(response.ContentHeaders!.ContentRange?.ToString(), Is.EqualTo($"bytes {start}-{bytes.Length - 1}/{bytes.Length}"));
        Assert.That(await response.Content!.ReadAsByteArrayAsync(), Is.EqualTo(bytes[(int)start..]));
    }

    [Test]
    public async Task V2RangeRouteStreamsTheRange()
    {
        var (owner, _, file, bytes) = await UploadBigPayload();
        const long start = 4 * 1024 * 1024 - 1, length = 1024 * 1024;

        var response = await owner.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, PayloadKey,
            new Services.Drives.FileSystem.Base.FileChunk { Start = start, Length = length });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        Assert.That(response.ContentHeaders!.ContentLength, Is.EqualTo(length));
        Assert.That(response.ContentHeaders.ContentRange?.ToString(),
            Is.EqualTo($"bytes {start}-{start + length - 1}/{bytes.Length}"));
        Assert.That(await response.Content!.ReadAsByteArrayAsync(), Is.EqualTo(bytes[(int)start..(int)(start + length)]));
    }

    //

    private async Task<(OwnerSession owner, TargetDrive drive, (Guid DriveId, Guid FileId, Guid UniqueId) file, byte[] bytes)>
        UploadBigPayload()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var bytes = new byte[PayloadSize];
        Random.Shared.NextBytes(bytes);
        var payload = new TestPayloadDefinition
        {
            Key = PayloadKey,
            ContentType = "application/octet-stream",
            Content = bytes,
            Thumbnails = []
        };

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AppData.UniqueId = Guid.NewGuid();
        var manifest = new UploadManifest { PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList() };
        var response = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata, manifest, [payload]);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");

        var created = response.Content!;
        return ((OwnerSession)owner, spec.TargetDrive, (created.DriveId, created.FileId, metadata.AppData.UniqueId.Value), bytes);
    }

    // The V1 GET is the endpoint that honours a Range header; the V2 whole-payload routes ignore it
    private static async Task<HttpResponseMessage> GetV1WithRange(OwnerSession owner, TargetDrive drive,
        (Guid DriveId, Guid FileId, Guid UniqueId) file,
        RangeHeaderValue range)
    {
        var client = owner.Factory.CreateHttpClient(owner.Identity, out _);
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{OwnerApiPathConstants.DriveStorageV1}/payload?fileId={file.FileId}&alias={drive.Alias}&type={drive.Type}&key={PayloadKey}");
        request.Headers.Range = range;
        return await client.SendAsync(request);
    }

    private static void AssertContentRange(HttpResponseMessage response, long from, long to, long size)
    {
        Assert.That(response.Content.Headers.ContentRange?.ToString(), Is.EqualTo($"bytes {from}-{to}/{size}"));
        Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(to - from + 1));
    }

    private static async Task<string> Body(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? "" : await response.Content.ReadAsStringAsync();
}
