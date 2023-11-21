using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests.DriveApi.DirectDrive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public class UniversalUploadFileTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();

        PrepareAppClient().GetAwaiter().GetResult();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    private readonly Dictionary<string, IApiClientFactory> _appApiFactories = new(StringComparer.InvariantCultureIgnoreCase);

    private readonly TestIdentity _identity = TestIdentities.Pippin;

    private async Task PrepareAppClient()
    {
        //I need to prepare a drive an an app
        var ownerClient = _scaffold.CreateOwnerApiClient(_identity);

        // Prepare the app

        Guid appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest();
        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerClient.Apps.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerClient.Apps.RegisterAppClient(appId);

        _appApiFactories.Add("pippin", new AppApiClientFactory(appToken, appSharedSecret));
    }

    private IApiClientFactory GetFactory(ApiClientType clientType, string key)
    {
        switch (clientType)
        {
            case ApiClientType.OwnerApi:
                return new OwnerApiClientFactory(_scaffold.OldOwnerApi);

            case ApiClientType.AppApi:
                if (_appApiFactories.TryGetValue(key, out var factory))
                {
                    return factory;
                }

                throw new Exception($"Invalid test case.  Key: {key}");

            case ApiClientType.GuestApi:
            case ApiClientType.TransitSenderApi:
            default:
                throw new ArgumentOutOfRangeException(nameof(clientType), clientType, null);
        }
    }

    [Test]
    [TestCase(ApiClientType.OwnerApi, "pippin")]
    [TestCase(ApiClientType.AppApi, "pippin")]
    [Ignore("WIP")]
    public async Task CanUploadFileWithCorrectPermissions(ApiClientType clientType, string key)
    {
        var client = _scaffold.CreateOwnerApiClient(_identity);

        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        var factory = GetFactory(clientType, key);
        var uniDrive = new UniversalDriveApiClient(_identity, factory);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Key = "test_key_1",
                ContentType = "text/plain",
                Content = "some content for payload key 1".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new()
                    {
                        PixelHeight = 200,
                        PixelWidth = 200,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes200,
                    }
                }
            },
            new()
            {
                Key = "test_key_2",
                ContentType = "text/plain",
                Content = "other types of content for key 2".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new()
                    {
                        PixelHeight = 400,
                        PixelWidth = 400,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes400,
                    }
                }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await uniDrive.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await uniDrive.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);

        //test the headers payload info
        foreach (var testPayload in testPayloads)
        {
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
            Assert.IsTrue(testPayload.Thumbnails.Count == payload.Thumbnails.Count);
            Assert.IsTrue(testPayload.ContentType == payload.ContentType);
            //Assert.IsTrue(payload.LastModified); //TODO: how to test?
        }

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await uniDrive.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, definition.Content);

            // Check all the thumbnails
            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await client.DriveRedux.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }
        }
    }

    [Test]
    [TestCase(ApiClientType.OwnerApi, "pippin")]
    [TestCase(ApiClientType.AppApi, "pippin")]
    [Ignore("WIP")]
    public async Task GetPayloadUsingValidPayloadKeyButPayloadDoesNotExistReturns404(ApiClientType clientType, TestIdentity identity, TargetDrive targetDrive)
    {
        var factory = GetFactory(clientType, identity.OdinId);
        var uniDrive = new UniversalDriveApiClient(_identity, factory);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail1,
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await uniDrive.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await uniDrive.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);

        // now that we know we have a valid file with a few payloads
        var getRandomPayload = await uniDrive.GetPayload(uploadResult.File, "r3nd0m09");
        Assert.IsTrue(getRandomPayload.StatusCode == HttpStatusCode.NotFound, $"Status code was {getRandomPayload.StatusCode}");
    }
}