using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.DriveQuery;

/// <summary>
/// Port of <c>_Universal/DriveTests/Query/AppQueryTests</c>. An app's query results are bounded by the
/// drive grant it holds, even on an anonymous-readable drive: ReadWrite and Read see both of Frodo's
/// posts, while Write, Comment and React each see only one — the app without a read grant is limited
/// to what any anonymous caller could see.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> query endpoint through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through each app session's <c>V1.Drive</c>.
///
/// No caller matrix — the original had none. It is a single <c>[Test]</c> that builds five app tokens
/// in a row and compares their result counts, so the permission sweep is the body rather than the
/// <c>[TestCaseSource]</c>. The apps are built with <see cref="AppSession.SetupAsync"/> directly
/// rather than through <see cref="CallerSpec"/>, because the drive they are granted on is created by
/// <see cref="QueryScenario"/> rather than by <c>SetupCaller</c>.
///
/// Sam and Pippin are hosted because <see cref="QueryScenario"/> genuinely connects them to Frodo — the
/// ACL on the posts names a circle, and a circle with no members is not the scenario under test.
/// </remarks>
[TestFixture]
public class AppQueryTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    [Test]
    public async Task AppQueryBatchEnforcesPermissionsOnAnonymousDrive()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);

        var scenarioConfig = await QueryScenario.ConfigureScenario1Async(frodo, sam, pippin);

        var query = new QueryBatchRequest()
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = scenarioConfig.TargetDrive,
                FileType = [scenarioConfig.FileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        // Frodo queries drive via app that has read write access to drive
        var appReadWrite = await AppSession.SetupAsync(frodo, scenarioConfig.TargetDrive, DrivePermission.ReadWrite);
        var readWriteQueryResults = await appReadWrite.V1.Drive.QueryBatch(query);
        Assert.That(readWriteQueryResults.IsSuccessStatusCode, Is.True);
        Assert.That(readWriteQueryResults.Content.SearchResults.Count(), Is.EqualTo(2));

        var appReadOnly = await AppSession.SetupAsync(frodo, scenarioConfig.TargetDrive, DrivePermission.Read);
        var readOnlyQueryResults = await appReadOnly.V1.Drive.QueryBatch(query);
        Assert.That(readOnlyQueryResults.IsSuccessStatusCode, Is.True);
        Assert.That(readOnlyQueryResults.Content.SearchResults.Count(), Is.EqualTo(2));

        var appWriteOnly = await AppSession.SetupAsync(frodo, scenarioConfig.TargetDrive, DrivePermission.Write);
        var writeOnlyQueryResults = await appWriteOnly.V1.Drive.QueryBatch(query);
        Assert.That(writeOnlyQueryResults.IsSuccessStatusCode, Is.True);
        Assert.That(writeOnlyQueryResults.Content.SearchResults.Count(), Is.EqualTo(1));

        var appCommentOnly = await AppSession.SetupAsync(frodo, scenarioConfig.TargetDrive, DrivePermission.Comment);
        var commentOnlyQueryResults = await appCommentOnly.V1.Drive.QueryBatch(query);
        Assert.That(commentOnlyQueryResults.IsSuccessStatusCode, Is.True);
        Assert.That(commentOnlyQueryResults.Content.SearchResults.Count(), Is.EqualTo(1));

        var appReactOnly = await AppSession.SetupAsync(frodo, scenarioConfig.TargetDrive, DrivePermission.React);
        var reactOnlyQueryResults = await appReactOnly.V1.Drive.QueryBatch(query);
        Assert.That(reactOnlyQueryResults.IsSuccessStatusCode, Is.True);
        Assert.That(reactOnlyQueryResults.Content.SearchResults.Count(), Is.EqualTo(1));
    }
}
