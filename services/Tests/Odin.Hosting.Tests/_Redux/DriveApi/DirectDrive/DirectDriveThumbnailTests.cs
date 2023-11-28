using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests._Redux.DriveApi.DirectDrive;

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
    public void ThumbnailLastModifiedTimeUpdatedWhenThumbnailChanges()
    {
        //upload a payload with a new payload
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