using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class AppQueryTests
{
    private WebScaffold _scaffold;

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
    public async Task AppQueryBatchEnforcesPermissionsOnAnonymousDrive()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var scenarioConfig = await Scenario.ConfigureScenario1(_scaffold);

        var query = new QueryBatchRequest()
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = scenarioConfig.TargetDrive,
                FileType = [scenarioConfig.FileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var appReadWrite = await CreateAppTokenFactory(frodoOwnerClient, scenarioConfig.TargetDrive, DrivePermission.ReadWrite);

        // Frodo queries drive via app that has read write access to drive
        var appDriveClientReadWrite = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, appReadWrite);
        var readWriteQueryResults = await appDriveClientReadWrite.QueryBatch(query);
        Assert.IsTrue(readWriteQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(readWriteQueryResults.Content.SearchResults.Count() == 2);
        
        var appReadOnly = await CreateAppTokenFactory(frodoOwnerClient, scenarioConfig.TargetDrive, DrivePermission.Read);
        var appDriveClientReadOnly = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, appReadOnly);
        var readOnlyQueryResults = await appDriveClientReadOnly.QueryBatch(query);
        Assert.IsTrue(readOnlyQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(readOnlyQueryResults.Content.SearchResults.Count() == 2);
        
        var appWriteOnly = await CreateAppTokenFactory(frodoOwnerClient, scenarioConfig.TargetDrive, DrivePermission.Write);
        var appDriveClientWriteOnly = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, appWriteOnly);
        var writeOnlyQueryResults = await appDriveClientWriteOnly.QueryBatch(query);
        Assert.IsTrue(writeOnlyQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(writeOnlyQueryResults.Content.SearchResults.Count() == 1);
        
        var appCommentOnly = await CreateAppTokenFactory(frodoOwnerClient, scenarioConfig.TargetDrive, DrivePermission.Comment);
        var appDriveCommentOnly = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, appCommentOnly);
        var commentOnlyQueryResults = await appDriveCommentOnly.QueryBatch(query);
        Assert.IsTrue(commentOnlyQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(commentOnlyQueryResults.Content.SearchResults.Count() == 1);
        
        var appReactOnly = await CreateAppTokenFactory(frodoOwnerClient, scenarioConfig.TargetDrive, DrivePermission.React);
        var appDriveReactOnly = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, appReactOnly);
        var reactOnlyQueryResults = await appDriveReactOnly.QueryBatch(query);
        Assert.IsTrue(reactOnlyQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(reactOnlyQueryResults.Content.SearchResults.Count() == 1);
        
    }

    private async Task<IApiClientFactory> CreateAppTokenFactory(OwnerApiClientRedux ownerClient, TargetDrive targetDrive, DrivePermission drivePermission)
    {
        var appContext = new AppSpecifyDriveAccess(targetDrive, drivePermission, new TestPermissionKeyList());
        await appContext.Initialize(ownerClient);
        return appContext.GetFactory();
    }
}