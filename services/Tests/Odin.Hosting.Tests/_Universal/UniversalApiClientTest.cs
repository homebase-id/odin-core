using System;
using System.Collections.Generic;
using System.Linq;
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
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal;

public enum ApiClientType
{
    Owner,
    App,
    Guest,
    TransitSender
}

public class UniversalApiClientTest
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

    private Dictionary<string, IApiClientFactory> _appApiFactories = new(StringComparer.InvariantCultureIgnoreCase);

    public async Task PrepareAppClient()
    {
        //I need to prepare a drive an an app
        var identity = TestIdentities.Pippin;
        var ownerClient = _scaffold.CreateOwnerApiClient(identity);

        // Prepare the app

        Guid appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest();
        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerClient.Apps.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerClient.Apps.RegisterAppClient(appId);

        _appApiFactories.Add("pippin", new AppApiClientFactory(appToken, appSharedSecret));
    }


    public IApiClientFactory GetFactory(ApiClientType clientType, string key)
    {
        switch (clientType)
        {
            case ApiClientType.Owner:
                return new OwnerApiClientFactory(_scaffold.OldOwnerApi);

            case ApiClientType.App:
                if (_appApiFactories.TryGetValue(key, out var factory))
                {
                    return factory;
                }

                throw new Exception($"Invalid test case.  Key: {key}");

            case ApiClientType.Guest:
            case ApiClientType.TransitSender:
            default:
                throw new ArgumentOutOfRangeException(nameof(clientType), clientType, null);
        }
    }

    [Test]
    [TestCase(ApiClientType.Owner, "pippin")]
    [TestCase(ApiClientType.App, "pippin")]
    [TestCase(ApiClientType.Guest, "pippin")]
    public async Task CanUploadFile(ApiClientType clientType, string key)
    {
        var identity = TestIdentities.Pippin;
        var client = _scaffold.CreateOwnerApiClient(identity);

        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        var uniDrive = new UniversalDriveApiClient(identity, GetFactory(clientType, key));

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
}