using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests.AppAPI.Drive.MultiPayload;

public class DriveMultiPayloadTests
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
    public void FailWhenDuplicatePayloadKeys()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void DeletingFileDeletesAllPayloadsAndThumbnails()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void PayloadMustIncludeAPayloadKeyAsMultipartUpload()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void CanUploadPayloadOrThumbnailsInAnyOrder()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void TransitSendsMultiplePayloads_When_SentViaDriveUpload()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void TransitSendsMultiplePayloads_When_SentViaTransitSender()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void TransitDistributesUpdatesWhenAtLeastOnePayloadIsChanged()
    {
        //Note - i think this i actually required by the client ot just send the whole thing
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void PayloadSizeIsSumOfAllPayloads()
    {
        Assert.Inconclusive("TODO: firstly, determine if bishwa or stef use this field");
    }

    [Test]
    public void GetPayloadReturns_NotFound_WhenKeyDoesNotExist()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void CanGetPayloadByKey()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void CanGetDefaultPayload_withEmptyKey()
    {
        Assert.Inconclusive("TODO");
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
    public void ClientFileMetadataIncludesPayloadDescriptors()
    {
        // ClientFileMetadata.Payloads 
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void FailsWhenInvalidPayloadKeyOrContentTypeIsSetOnAnyPayloads()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void FileMetadataPayloadsListIsUpdatedWhenPayloadIsDeleted()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void PayloadLastModifiedTimeUpdatedWhenPayloadChanges()
    {
        Assert.Inconclusive("TODO");
    }
    
    [Test]
    public void MetadataIsUpdatedWhenPayloadIsDeleted()
    {
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
    public void MetadataLastModifiedDateIsUpdatedWhenPayloadAddedUpdatedOrRemoved()
    {
        Assert.Inconclusive("TODO");
    }
}