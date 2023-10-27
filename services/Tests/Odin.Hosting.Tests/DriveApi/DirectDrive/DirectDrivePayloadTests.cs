using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests.DriveApi.DirectDrive;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayloadTests
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
    public void CanUploadFileWithMultiplePayloads()
    {
        // create a drive
        // upload metadata, thumbnail, and payload
        
        // get the file header
        
        // get the thumbnail
        
        // get the payload
        
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void CanAddPayloadToExistingFileAndMetadataIsAutomaticallyUpdated()
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
    public void CanModifyPayloadToExistingFileAndMetadataIsAutomaticallyUpdated()
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
    public void CanDeletePayloadToExistingFileAndMetadataIsAutomaticallyUpdated()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void FailWhenDuplicatePayloadKeys()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void PayloadMustIncludeAPayloadKeyAsMultipartUpload()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void PayloadSizeIsSumOfAllPayloads()
    {
        Assert.Inconclusive("TODO: firstly, determine if bishwa or stef use this field");
    }


    [Test]
    public void FailIfPayloadKeyIncludesInvalidChars()
    {
        Assert.Inconclusive("TODO: determine what is a valid key?  maybe just? [a-z][A-Z]-");
    }

    [Test]
    public void FailWhenInvalidContentTypeSpecified()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void GetPayloadByKeyIncludesCorrectHeaders()
    {
        // HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadKey, ps.Key);
        // HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, ps.ContentType);
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void AddingPayloadFailsWhenInvalidContentTypeSpecified()
    {
        Assert.Inconclusive("TODO");
    }
    
    
    [Test]
    public void GetPayloadReturns_NotFound_WhenKeyDoesNotExist()
    {
        Assert.Inconclusive("TODO");
    }
 
    
    [Test]
    public void FailsWhenInvalidPayloadKeyOrContentTypeIsSetOnAnyPayloads()
    {
        Assert.Inconclusive("TODO");
    }
}