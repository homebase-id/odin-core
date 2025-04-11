using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.DriveCore.Storage.Gugga;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests;

public class DefraggerTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(initializeIdentity: true);
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
        _scaffold.DumpLogEventsToConsole();
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task TestSwaggerIsUp()
    {
        var client = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);
        var result = await client.GetAsync("/swagger/v1/swagger.json");
        ClassicAssert.AreEqual(HttpStatusCode.OK, result.StatusCode);

        var loggerMock = new Mock<ILogger<Defragmenter>>();

        var dfrw = _scaffold.Services.GetRequiredService<DriveFileReaderWriter>();
        var mta = _scaffold.Services.GetRequiredService<IMultiTenantContainerAccessor>();

        var dq = mta.GetTenantScope(TestIdentities.Samwise.OdinId).Resolve<DriveQuery>();


        var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        var defragmenter = new Defragmenter(loggerMock.Object, dfrw, dq);

        var drives = (await ownerClient.Drive.GetDrives(1, 100)).Content.Results;

        foreach(var drive in drives) 
        {
            var sd = new StorageDrive(); // Todd, I need this object to call my own function...
            var fst = Core.Storage.FileSystemType.Standard; // And I need this value picked out of the drive too somehow

            // Todd if you're in a flow, please show me how I might iterate over each file on the drive.
            // If I e.g. have the MainIndexMeta then I could call QueryBatch(), so you could show me how
            // to get the MainIndexMeta object and I can take it from there.

            //I'll eventually create a loop myself here and iterate over each file
            await defragmenter.DefragmentFileAsync(sd, Guid.Empty, fst);
        }
    }    
}