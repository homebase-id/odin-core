using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests.DriveApi.TransitDrive;

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
    
}