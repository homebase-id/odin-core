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
    [Explicit]
    public async Task DefragDriveTest()
    {
        var ownerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var t = await ownerClient.DriveManager.GetDrives();
        var drives = t.Content.Results;

        // XXX Todd
        // Please upload a header + a payload and a thumb or two

        foreach (var drive in drives)
        {
            // this calls to the server and on the server side you will perform the defrag
            // doing it this way ensures all context and all services are setup correclty
            await ownerClient.DriveManager.Defrag(drive.TargetDriveInfo);
        }
    }
}
