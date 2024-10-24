using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

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
        Assert.IsTrue(samOwnerClientQueryResponse.IsSuccessStatusCode);
        Assert.IsTrue(samOwnerClientQueryResponse.Content.SearchResults.Count() == 2);

        // Pippin queries frodo over peer (as he would from his owner feed)
        var pippinOwnerClientQueryResponse = await pippinOwnerClient.PeerQuery.GetBatch(peerQueryBatchRequest);
        Assert.IsTrue(pippinOwnerClientQueryResponse.IsSuccessStatusCode);
        Assert.IsFalse(pippinOwnerClientQueryResponse.Content.SearchResults.Any());

        // Merry queries frodo over peer (as he would from his owner feed)
        var merryOwnerClientQueryResponse = await merryOwnerClient.PeerQuery.GetBatch(peerQueryBatchRequest);
        Assert.IsTrue(merryOwnerClientQueryResponse.IsSuccessStatusCode);
        Assert.IsFalse(merryOwnerClientQueryResponse.Content.SearchResults.Any());
    }
}