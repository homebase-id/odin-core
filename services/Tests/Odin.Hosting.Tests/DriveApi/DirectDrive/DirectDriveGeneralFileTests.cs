using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.Management;

namespace Odin.Hosting.Tests.DriveApi.DirectDrive;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveGeneralFileTests
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

    [Test]
    public async Task CanUploadFileWith2ThumbnailsAnd2Payloads()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                ContentIsComplete = true,
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var response = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadedFileMetadata);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        Assert.IsTrue(getHeaderResponse.Content.FileMetadata.AppData.JsonContent == uploadedFileMetadata.AppData.JsonContent);
    }

    [Test]
    public void CanUpdateMetadataDataWithOutThumbnailsAndWithoutPayloads()
    {
        // create a drive
        // upload metadata

        // get the file header
        // thumbnail list and payload list should be empty


        Assert.Inconclusive("TODO");
    }

    [Test]
    public void DeletingFileDeletesAllPayloadsAndThumbnails()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void CanUploadPayloadOrThumbnailsInAnyOrder()
    {
        Assert.Inconclusive("TODO");
    }
}