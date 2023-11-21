using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public class UniversalUploadFileTest_2
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

    // private const string _alias = "d8e3bb87-83b2-4086-9fb9-82fda0e7e106";
    // private const string _type = "0003bb87-83b2-4086-9fb9-82fda0e7e000";

    private static TargetDrive _targetDrive = new()
    {
        Alias = Guid.Parse("d8e3bb87-83b2-4086-9fb9-82fda0e7e106"),
        Type = Guid.Parse("0003bb87-83b2-4086-9fb9-82fda0e7e000")
    };

    public async Task PrepareAppClient()
    {
        //I need to prepare a drive an an app
        var identity = TestIdentities.Pippin;
        var ownerClient = _scaffold.CreateOwnerApiClient(identity);

        // create a drive
        await ownerClient.Drive.CreateDrive(_targetDrive, "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // Prepare the app

        Guid appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = _targetDrive,
                        Permission = DrivePermission.Write
                    }
                }
            }
        };

        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerClient.Apps.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerClient.Apps.RegisterAppClient(appId);

        _appApiFactories.Add(identity.OdinId, new AppApiClientFactory(appToken, appSharedSecret));
    }

    public IApiClientFactory GetFactory(ApiClientType clientType, string key)
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

    private static IEnumerable<TestCaseData> TestData()
    {
        yield return new TestCaseData(ApiClientType.OwnerApi, TestIdentities.Pippin, _targetDrive, HttpStatusCode.OK);
        yield return new TestCaseData(ApiClientType.AppApi, TestIdentities.Pippin, _targetDrive, HttpStatusCode.OK);
    }

    [Test]
    [TestCaseSource(nameof(TestData))]
    public async Task CanUploadFileWithCorrectPermissions(ApiClientType clientType, TestIdentity identity, TargetDrive targetDrive,
        HttpStatusCode expectedStatusCode)
    {
        var factory = GetFactory(clientType, identity.OdinId);
        var uniDrive = new UniversalDriveApiClient(identity.OdinId, factory);

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

        var response = await uniDrive.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.StatusCode == expectedStatusCode);
        // Assert.IsTrue(response.IsSuccessStatusCode);
    }
}