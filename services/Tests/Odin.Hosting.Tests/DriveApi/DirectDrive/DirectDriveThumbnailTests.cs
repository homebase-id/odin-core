using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests.DriveApi.DirectDrive;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveThumbnailTests
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
    public void CanUploadFileWithMultipleThumbnails()
    {
        // create a drive
        // upload metadata, thumbnail, and payload
        
        // get the file header
        
        // get the thumbnail
        
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void CanAddThumbnailToExistingFileAndMetadataIsAutomaticallyUpdated()
    {
        // create a drive
        // upload metadata, no payload
        
        //add payload
        
        // get the file header
        // last modified should be changed
        // payload should be listed 
        // get the payload
        
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void CanRemoveThumbnailToExistingFileAndMetadataIsAutomaticallyUpdated()
    {
        // create a drive
        // upload metadata, no payload
        
        //add payload
        
        // get the file header
        // last modified should be changed
        // payload should be listed 
        // get the payload
        
        Assert.Inconclusive("TODO");
    }
    
    
    [Test]
    public void ThumbnailLastModifiedTimeUpdatedWhenThumbnailChanges()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void ThumbnailsAreAutomaticallyDetectedDuringUpload()
    {
        // read from Thumbnails on ClientFileHeader instead fo AppData
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void AddingThumbnailFailsWhenInvalidContentTypeSpecified()
    {
        Assert.Inconclusive("TODO");
    }


}