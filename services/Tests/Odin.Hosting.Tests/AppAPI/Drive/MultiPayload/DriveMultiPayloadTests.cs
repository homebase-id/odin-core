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
    public void FailIfPayloadKeyIncludesInvalidChars()
    {
        Assert.Inconclusive("TODO: determine what is a valid key?  maybe just? [a-z][A-Z]-");
    }
}