using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Schema;
using Microsoft.VisualStudio.TestPlatform;
using NUnit.Framework;
using Odin.Core.Cryptography;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Redux.DriveApi.DirectDrive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

/*
 * A scenario gives the ability to vary the client performing the primary action with in a test
 * the primary action does not include all of the setup required to prepare the environment but
 * rather it is the THING being tested
 *
 *
 */

public class UniversalUploadFileTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    public static IEnumerable TestCases()
    {
        // var pk = new TestPermissionKeyList(PermissionKeys.ReadConnections, PermissionKeys.UseTransitRead);
        // yield return new object[] { new AppWriteOnlyAccessToDrive(pk) };
        return null;
    }
    

    // [Test]
    // [TestCase(typeof(AppNoAccessToDrive))]
    // [TestCase(typeof(GuestDomainReadonlyAccessToDrive))]
    public async Task ReceivesCorrectErrorWhenFileUploadedWithoutPermission(Type clientApiFactory)
    {
        // var identity = TestIdentities.Pippin;
        //
        // var client = _scaffold.CreateOwnerApiClient(identity);
        //
        // // create a drive
        // var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true);
        //
        // var scenario = ScenarioUtil.Instantiate(clientApiFactory);
        // await scenario.Initialize(client, targetDrive.TargetDriveInfo);
        //
        // // upload metadata
        //
        // var uniDrive = new UniversalDriveApiClient(identity.OdinId, scenario.GetFactory());
        // var uploadedFileMetadata = SampleMetadataDataDefinitions.Create(fileType: 100);
        // var testPayloads = new List<TestPayloadDefinition>()
        // {
        //     SamplePayloadDefinitions.PayloadDefinition1,
        //     SamplePayloadDefinitions.PayloadDefinition2
        // };
        //
        // var uploadManifest = new UploadManifest()
        // {
        //     PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        // };
        //
        // var response = await uniDrive.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);
        //
        // Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);
    }

    // [Test]
    // [TestCase(typeof(AppWriteOnlyAccessToDrive))]
    // [TestCase(typeof(AppReadonlyAccessToDrive))]
    public async Task GetPayloadReturns404WhenFileExistsButPayloadDoesNot(Type scenarioType)
    {
        // var identity = TestIdentities.Pippin;
        //
        // // create a drive
        // var ownerClient = _scaffold.CreateOwnerApiClient(identity);
        // var targetDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true);
        //
        // // upload metadata
        //
        // var uploadedFileMetadata = SampleMetadataDataDefinitions.Create(fileType: 100);
        // var testPayloads = new List<TestPayloadDefinition>()
        // {
        //     TestPayloadDefinitions.PayloadDefinitionWithThumbnail1,
        //     TestPayloadDefinitions.PayloadDefinitionWithThumbnail2
        // };
        //
        // var uploadManifest = new UploadManifest()
        // {
        //     PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        // };
        //
        // var response = await ownerClient.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);
        //
        // Assert.IsTrue(response.IsSuccessStatusCode);
        // var uploadResult = response.Content;
        // Assert.IsNotNull(uploadResult);
        //
        // // get the file header
        // var getHeaderResponse = await ownerClient.DriveRedux.GetFileHeader(uploadResult.File);
        // Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        // var header = getHeaderResponse.Content;
        // Assert.IsNotNull(header);
        // Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
        //
        // // now that we know we have a valid file with a few payloads
        //
        // var scenario = ScenarioUtil.Instantiate(scenarioType);
        // await scenario.Initialize(ownerClient, targetDrive.TargetDriveInfo);
        // var uniDrive = new UniversalDriveApiClient(identity.OdinId, scenario.GetFactory());
        //
        // var getRandomPayload = await uniDrive.GetPayload(uploadResult.File, "r3nd0m09");
        // Assert.IsTrue(getRandomPayload.StatusCode == HttpStatusCode.NotFound, $"Status code was {getRandomPayload.StatusCode}");
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanUploadFile(IApiClientContext clientFactory)
    {
        var identity = TestIdentities.Pippin;

        // create a drive
        var ownerClient = _scaffold.CreateOwnerApiClient(identity);
        var targetDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata

        await clientFactory.Initialize(ownerClient);
        var uniDrive = new UniversalDriveApiClient(identity.OdinId, clientFactory.GetFactory());

        var uploadedFileMetadata = SampleMetadataDataDefinitions.Create(fileType: 100);
        var testPayloads = new List<TestPayloadDefinition>()
        {
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail1,
            TestPayloadDefinitions.PayloadDefinitionWithThumbnail2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await uniDrive.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);
    }
}