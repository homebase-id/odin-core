using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class GuestQueryTests
{
    private WebScaffold _scaffold;

    [SetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [TearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    // [Test]
    // [Ignore("wip")]
    // public async Task QueryBatchEnforcesPermissionsOnAnonymousDrive()
    // {
      
        // // Sam Queries pippin; should get both files
        // var samGuestTokenFactory = await CreateGuestTokenFactory(TestIdentities.Samwise, frodoOwnerClient, targetDrive);
        // var samDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, samGuestTokenFactory);
        // var samQueryResults = await samDriveClient.QueryBatch(query);
        // Assert.IsTrue(samQueryResults.IsSuccessStatusCode);
        // Assert.IsTrue(samQueryResults.Content.SearchResults.Count() == 2);
        //
        // // Merry queries Pippin; should get file2 only
        // var merryGuestTokenFactory = await CreateGuestTokenFactory(TestIdentities.Merry, frodoOwnerClient, targetDrive);
        // var merryDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, merryGuestTokenFactory);
        // var merryQueryResults = await merryDriveClient.QueryBatch(query);
        // Assert.IsTrue(merryQueryResults.IsSuccessStatusCode);
        // Assert.IsTrue(merryQueryResults.Content.SearchResults.Count() == 1);

        //
    // }

    

}