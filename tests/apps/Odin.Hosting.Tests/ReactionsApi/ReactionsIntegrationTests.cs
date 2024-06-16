using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.ReactionsApi;

public class ReactionsIntegrationTests
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

    #region list - get all reactions

    [Test]
    public async Task CanGetEmptyReactionList()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var drives = await frodoOwnerClient.DriveManager.GetDrives();
        var publicPostsDrive =
            drives.Content.Results.Single(
                d => d.TargetDriveInfo.Alias == SystemDriveConstants.PublicPostsChannelDrive.Alias &&
                     d.TargetDriveInfo.Type == SystemDriveConstants.PublicPostsChannelDrive.Type);

        // const string friendsOnlyContent = "some secured friends only content";
        // var friendsFile = SampleMetadataData.CreateWithContent(postFileType, friendsOnlyContent, AccessControlList.Connected);
        // friendsFile.AllowDistribution = true;
        // var (friendsFileUploadResponse, encryptedJsonContent64) = await samOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
        //     friendsOnlyTargetDrive,
        //     friendsFile);
        //
        // Assert.IsTrue(friendsFileUploadResponse.IsSuccessStatusCode);

        ;


        // frodoOwnerClient.

    }

    #endregion


}