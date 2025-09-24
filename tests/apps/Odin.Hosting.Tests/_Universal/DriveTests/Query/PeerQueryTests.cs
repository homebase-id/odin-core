using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class PeerQueryTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin, TestIdentities.Samwise, TestIdentities.Merry, TestIdentities.Frodo });
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
    public async Task PeerQueryBatchEnforcesPermissionsOnAnonymousDrive()
    {
        // var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        var  scenarioConfig = await Scenario.ConfigureScenario1(_scaffold);

        var peerQueryBatchRequest = new PeerQueryBatchRequest()
        {
            OdinId = TestIdentities.Frodo.OdinId,
            QueryParams = new FileQueryParams()
            {
                TargetDrive = scenarioConfig.TargetDrive,
                FileType = [scenarioConfig.FileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        // Assert
        // Sam queries frodo over peer (as he would from his owner feed)
        var samOwnerClientQueryResponse = await samOwnerClient.PeerQuery.GetBatch(peerQueryBatchRequest);
        ClassicAssert.IsTrue(samOwnerClientQueryResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(samOwnerClientQueryResponse.Content.SearchResults.Count() == 2);

        // Pippin queries frodo over peer (as he would from his owner feed)
        var pippinOwnerClientQueryResponse = await pippinOwnerClient.PeerQuery.GetBatch(peerQueryBatchRequest);
        ClassicAssert.IsTrue(pippinOwnerClientQueryResponse.IsSuccessStatusCode);
        ClassicAssert.IsFalse(pippinOwnerClientQueryResponse.Content.SearchResults.Any());

        // Merry queries frodo over peer (as he would from his owner feed)
        var merryOwnerClientQueryResponse = await merryOwnerClient.PeerQuery.GetBatch(peerQueryBatchRequest);
        ClassicAssert.IsTrue(merryOwnerClientQueryResponse.IsSuccessStatusCode);
        ClassicAssert.IsFalse(merryOwnerClientQueryResponse.Content.SearchResults.Any());
    }
}