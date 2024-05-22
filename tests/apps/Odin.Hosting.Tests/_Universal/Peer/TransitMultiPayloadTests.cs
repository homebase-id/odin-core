using System.Collections;
using System.Net;
using System.Reflection;
using NUnit.Framework;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.Peer;

public class TransitMultiPayloadTests
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

    public static IEnumerable TestCases()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
    }

    // [Test]
    // [TestCaseSource(nameof(TestCases))]
    // public async Task TransitSendsMultiplePayloads_When_SentViaDriveUpload(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    // {
    // }

}