using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.Auth;

public class AuthTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin, TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    public static IEnumerable TestCases()
    {
        yield return new object[]
            { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK }; // guest falls back to anon
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Unauthorized };
        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.Unauthorized };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanVerifyTokenAndLogout(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        // Dummy drive required for the test cases
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", false);

        await callerContext.Initialize(ownerApiClient);
        var client = new AuthV2Client(identity.OdinId, callerContext.GetFactory());

        var response = await client.VerifyToken();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logoutResponse = await client.Logout();
        Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var verifyResponsePostLogout = await client.VerifyToken();
        ClassicAssert.IsTrue(verifyResponsePostLogout.StatusCode == expectedStatusCode,
            $"code was '{verifyResponsePostLogout.StatusCode}'");
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanVerifySharedSecretEncryption(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        // Dummy drive required for the test cases
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", false);

        await callerContext.Initialize(ownerApiClient);
        var client = new AuthV2Client(identity.OdinId, callerContext.GetFactory());

        const string checkValue = "bing bam bop";
        var bytes = checkValue.ToUtf8ByteArray();
        var expectedHash = SHA256.Create().ComputeHash(bytes);
        var response = await client.VerifySharedSecretEncryption(bytes.ToBase64());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(expectedHash, response.Content.FromBase64()), Is.True);
    }
}