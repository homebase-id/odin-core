using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.UpdateBatch;

public class HammerTimeDirectDriveLocalUpdateBatchTests
{
    private WebScaffold _scaffold;

    private OwnerApiClientRedux _ownerApiClient;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;

    private UploadFileMetadata _originalFileMetadata;
    private TestPayloadDefinition _originalPayload;
    private int _successCount;
    private int _conflictCount;
    
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        const string fixedSubPath = "wtfm8";
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder, fixedSubPath);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // _scaffold.RunAfterAnyTests();
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    public async Task UpdateBatch_HammerTime_WithPayloads(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Pippin;
        _ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await _ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);
        
        Console.WriteLine($"Data path: [{_scaffold.TestDataPath}/tenants/payloads/]");

        //
        // Setup - upload a new file with payloads 
        // 
        _originalFileMetadata = SampleMetadataData.Create(fileType: 100);
        _originalPayload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [_originalPayload.ToPayloadDescriptor()]
        };

        var uploadNewFileResponse = await _ownerApiClient.DriveRedux.UploadNewFile(targetDrive,
            _originalFileMetadata, uploadManifest, [_originalPayload]);

        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);

        var uploadResult = uploadNewFileResponse.Content;
        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(uploadResult);
        _targetFile = uploadResult!.File;
        _initialVersionTag = uploadResult.NewVersionTag;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 2, iterations: 100, OverwriteUsingUpdateBatch);

        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Conflict Count: {_conflictCount}");
    }

    private async Task<(long, long[])> OverwriteUsingUpdateBatch(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;
        Random random = new Random();

        Guid newVersionTag = _initialVersionTag;

        for (int count = 0; count < iterations; count++)
        {
            sw.Restart();

            // change around some data
            _originalFileMetadata.AppData.Content = "some new content here";
            _originalFileMetadata.AppData.DataType = 777;
            
            var randomPayloadContent = string.Join("", Enumerable.Range(2468, 2468).Select(_ => Guid.NewGuid().ToString("N")));

            _originalPayload.Content = randomPayloadContent.ToUtf8ByteArray();
            var testPayloads = new List<TestPayloadDefinition>()
            {
                _originalPayload
            };

            var prevTag = newVersionTag;
            var (status, oce, versionTag) = await UploadAndValidatePayload(newVersionTag, testPayloads);

            if (status == HttpStatusCode.OK)
            {
                ClassicAssert.IsTrue(versionTag.HasValue);
                newVersionTag = versionTag.GetValueOrDefault();
                ClassicAssert.IsTrue(prevTag != newVersionTag, "version tag did not change");
            }
            else
            {
                _conflictCount++;
                if (oce == OdinClientErrorCode.VersionTagMismatch)
                {
                    // we must presume there was a version tag mismatch, let's see if we can get back in the race
                    var getHeader = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
                    newVersionTag = getHeader.Content.FileMetadata.VersionTag;
                }
                else
                {
                    throw new Exception($"Error uploading payload: HttpStatus {status}, OdinClientErrorCode {oce}");
                }
            }

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            await Task.Delay(random.Next(5, 51));
        }

        return (fileByteLength, timers);
    }

    private async Task<(HttpStatusCode status, OdinClientErrorCode? oce, Guid? versionTag)> UploadAndValidatePayload(
        Guid targetVersionTag,
        List<TestPayloadDefinition> testPayloads)
    {
        _originalFileMetadata.VersionTag = targetVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = _targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                // in this test we are just overwriting payloads
                PayloadDescriptors = testPayloads.Select(tpd => tpd.ToPayloadDescriptor(
                    PayloadUpdateOperationType.AppendOrOverwrite)).ToList()
            }
        };
        
        var updateFileResponse = await _ownerApiClient.DriveRedux.UpdateFile(updateInstructionSet, _originalFileMetadata, [_originalPayload]);

        // bounce if it's a bad request
        if (updateFileResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            var oce = TestUtils.ParseProblemDetails(updateFileResponse.Error);
            return (updateFileResponse.StatusCode, oce, null);
        }

        _successCount++;
            
        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == _originalFileMetadata.AppData.Content);
        ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == _originalFileMetadata.AppData.DataType);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 1);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == _originalPayload.Key), "missing payload");

        //
        // Ensure payloadToAdd add is added
        //
        var getPayloadToAddResponse = await _ownerApiClient.DriveRedux.GetPayload(_targetFile, _originalPayload.Key);
        ClassicAssert.IsTrue(getPayloadToAddResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue);
        ClassicAssert.IsTrue(
            getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

        // var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
        // CollectionAssert.AreEqual(content, _originalPayload.Content);

        // Check all the thumbnails
        foreach (var thumbnail in _originalPayload.Thumbnails)
        {
            var getThumbnailResponse = await _ownerApiClient.DriveRedux.GetThumbnail(_targetFile, thumbnail.PixelWidth,
                thumbnail.PixelHeight, _originalPayload.Key);

            ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                 DateTimeOffset.Now.AddSeconds(10));

            var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
        }
        
        return (updateFileResponse.StatusCode, OdinClientErrorCode.NoErrorCode, updateFileResponse.Content!.NewVersionTag);
    }
}