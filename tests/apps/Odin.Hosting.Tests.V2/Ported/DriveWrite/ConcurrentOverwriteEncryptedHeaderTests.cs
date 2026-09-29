using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrive_Concurrent_Overwrite_Encrypted_Header</c>. Twenty
/// threads each overwrite their own encrypted header fifty times, carrying the version tag forward
/// from one write to the next. The claim under test is not a throughput number but that the server
/// never answers <c>500</c> under that load: a losing writer must be refused cleanly, not blow up.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> upload endpoint through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>owner.V1.Drive</c>.
///
/// The concurrency shape is carried over unchanged (<see cref="PerformanceFramework.ThreadedTestAsync"/>,
/// 20 threads x 50 iterations). Each thread's work is ordinary HTTP against the test server, so every
/// request gets its own DI scope — the <c>ScopedConnectionFactory</c> "parallelism detected" hazard
/// applies to sharing one lifetime scope across tasks, which nothing here does.
///
/// The drive is still created once up front rather than per thread, for the reason the original gives:
/// with twenty threads racing to create the same drive only one create wins, and a loser's first
/// upload can land before the winner's drive is visible.
/// </remarks>
[TestFixture]
public class ConcurrentOverwriteEncryptedHeaderTests : V2Fixture
{
    private OwnerSession _owner;
    private TargetDrive _targetDrive;

    private int _successCount;
    private int _serverErrorCount;
    private string _firstServerErrorBody;

    // Deliberately NOT [Ignore]d, unlike its two siblings. Twenty concurrent writers against one
    // drive make the V1 upload endpoint answer 500 on a small fraction of requests (#1780, high
    // priority). The fixture's claim is that a losing writer is refused cleanly rather than blowing
    // up, so the assertion is right and the product is what is wrong. windows/sqlite/debug is left
    // red on purpose: the failure is a real product defect, and hiding it behind [Ignore] would buy
    // a green board at the price of the signal. See docs/flakytests.md and #1780.
    [Test]
    public async Task Overwrite_Encrypted_PayloadManyTimes_Concurrently_MultipleThreads()
    {
        _owner = await LoginAsOwner();
        _targetDrive = TargetDrive.NewTargetDrive();

        // Create the drive once, up front. All threads share this drive, so creating it from inside
        // each thread is a race: only one create wins, and a loser's first upload can land before the
        // winner's drive is visible - which surfaces as a non-success upload in PrepareEncryptedFile.
        await _owner.Admin.CreateDrive(_targetDrive, "Test Drive 001", allowAnonymousReads: true);

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 20, iterations: 50, OverwriteFile);
        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Bad Request Count: {_serverErrorCount}");

        Assert.That(_serverErrorCount, Is.EqualTo(0));
    }

    private async Task<(long, long[])> OverwriteFile(int threadNumber, int iterations)
    {
        //
        // Prepare by uploading a file
        //
        var (uploadResult, _) = await PrepareEncryptedFile(_targetDrive);
        var targetFile = uploadResult.File;
        var initialVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _owner.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderBeforeUploadResponse.IsSuccessStatusCode, Is.True);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.That(headerBeforeUpload, Is.Not.Null);

        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;

        var newVersionTag = initialVersionTag;

        for (int count = 0; count < iterations; count++)
        {
            sw.Restart();

            var prevTag = newVersionTag;
            var (tag, status) = await UploadAndValidateHeader(targetFile, newVersionTag);

            // Assert on the status, not on tag.HasValue: a bare "Expected: True, But was: False"
            // says an upload failed but not how. This earned its keep immediately -- the next CI run
            // reported InternalServerError on six named iterations, which is what identified #1780.
            // (An earlier version of this comment blamed #1777's SQLite write contention. That was a
            // guess, and the log from the failing run carries no lock error at all; see #1780.)
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK),
                $"upload failed on iteration {count} of thread body. First 500 body: {_firstServerErrorBody ?? "(none captured)"}");
            newVersionTag = tag.GetValueOrDefault();
            Assert.That(newVersionTag, Is.Not.EqualTo(prevTag), $"version tag did not change on iteration {count}");

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;
        }

        return (fileByteLength, timers);
    }

    private async Task<(Guid? Tag, HttpStatusCode Status)> UploadAndValidateHeader(
        ExternalFileIdentifier targetFile, Guid targetVersionTag)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            VersionTag = targetVersionTag,
            AppData = new UploadAppFileMetaData()
            {
                Content = $"new content {Guid.NewGuid()}"
            }
        };

        var storageOptions = new StorageOptions
        {
            Drive = targetFile.TargetDrive,
            OverwriteFileId = targetFile.FileId,
            StorageIntent = StorageIntent.NewFileOrOverwrite
        };

        var (uploadPayloadResponse, _) =
            await _owner.V1.Drive.UploadNewEncryptedMetadata(fileMetadata, storageOptions, transitOptions: null);

        if (uploadPayloadResponse.IsSuccessStatusCode)
        {
            Interlocked.Increment(ref _successCount);
            Assert.That(uploadPayloadResponse.Content!.NewVersionTag, Is.Not.EqualTo(targetVersionTag),
                "Version tag should have changed");
            return (uploadPayloadResponse.Content!.NewVersionTag, uploadPayloadResponse.StatusCode);
        }

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.InternalServerError)
        {
            Interlocked.Increment(ref _serverErrorCount);

            // Keep the first 500's body. The status alone does not name the defect, and the host's
            // own error log did not reach the CI output on the runs that failed (see #1780), so
            // without this the next failure is again a status code with no cause behind it.
            Interlocked.CompareExchange(ref _firstServerErrorBody,
                uploadPayloadResponse.Error?.Content ?? "(no response body)", null);
        }

        return (null, uploadPayloadResponse.StatusCode);
    }

    private async Task<(UploadResult, KeyHeader keyHeader)> PrepareEncryptedFile(TargetDrive targetDrive)
    {
        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "just some test data";
        var keyHeader = KeyHeader.NewRandom16();

        var (uploadNewMetadataResponse, _) = await _owner.V1.Drive.UploadNewEncryptedMetadata(
            targetDrive,
            uploadedFileMetadata,
            keyHeader: keyHeader);

        Assert.That(uploadNewMetadataResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        return (uploadResult, keyHeader);
    }
}
