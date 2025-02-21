using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.Performance;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrive_Concurrent_Overwrite_Encrypted_Header
{
    private WebScaffold _scaffold;

    private OwnerApiClientRedux _ownerApiClient;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;
    private KeyHeader _metadataKeyHeader;

    private int _successCount;
    private int _serverErrorCount;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


    [Test]
    public async Task Overwrite_Encrypted_PayloadManyTimes_Concurrently_MultipleThreads()
    {
        var identity = TestIdentities.Pippin;
        var targetDrive = TargetDrive.NewTargetDrive();
        _ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        //
        // Prepare by uploading a file
        //
        var (uploadResult, metadataKeyHeader) = await PrepareEncryptedFile(identity, targetDrive);
        _targetFile = uploadResult.File;
        _initialVersionTag = uploadResult.NewVersionTag;
        _metadataKeyHeader = metadataKeyHeader;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforeUpload);

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 20, iterations: 50, OverwriteFile);
        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Bad Request Count: {_serverErrorCount}");

        ClassicAssert.IsTrue(_serverErrorCount == 0, $"Server error count was {_serverErrorCount}");
    }

    private async Task<(long, long[])> OverwriteFile(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;

        var newVersionTag = _initialVersionTag;

        for (int count = 0; count < iterations; count++)
        {
            sw.Restart();

            var prevTag = newVersionTag;
            var tag = await UploadAndValidateHeader(_targetFile, newVersionTag);

            if (tag.HasValue)
            {
                newVersionTag = tag.GetValueOrDefault();
                ClassicAssert.IsTrue(prevTag != newVersionTag, "version tag did not change");
            }

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            // If you want to introduce a delay be sure to use: await Task.Delay(1);
            await Task.Delay(100);
            // Take.Delay() is very inaccurate.
        }

        return (fileByteLength, timers);
    }

    private async Task<Guid?> UploadAndValidateHeader(ExternalFileIdentifier targetFile, Guid targetVersionTag)
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

        var (uploadPayloadResponse, encryptedPayloads64) =
            await _ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(fileMetadata, storageOptions, transitOptions: null);

        if (uploadPayloadResponse.IsSuccessStatusCode)
        {
            _successCount++;
            // if it 
            ClassicAssert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");
            return uploadPayloadResponse.Content!.NewVersionTag;
        }

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.InternalServerError)
        {
            _serverErrorCount++;
        }

        return null;
    }

    private async Task<(UploadResult, KeyHeader keyHeader)> PrepareEncryptedFile(TestIdentity identity, TargetDrive targetDrive)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "just some test data";
        var keyHeader = KeyHeader.NewRandom16();

        var (uploadNewMetadataResponse, encryptedJsonContent64) = await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(
            targetDrive,
            uploadedFileMetadata,
            keyHeader: keyHeader);

        ClassicAssert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        return (uploadResult, keyHeader);
    }
}