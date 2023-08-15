using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.KeyChainDatabase;

namespace Odin.KeyChainTests.Integration.Controllers;

public class RegisterKeyControllerTest
{
    private KeyChainWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _factory = new KeyChainWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose(); // we need this to correctly dispose of the key chain database
    }

    [Test]
    public async Task GetVerifyShouldReturnIdentityAge()
    {
        // Arrange
        const string identity = "frodo.baggins.me";
        SeedDatabase(identity);

        // Act
        var response = await _client.GetAsync($"/RegisterKey/Verify?identity={identity}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(int.Parse(content), Is.GreaterThanOrEqualTo(0));
    }

    //

    [Test]
    public async Task GetVerifyShouldReturnNotFoundForUnknownIdentities()
    {
        // Arrange
        // ...

        // Act
        var response = await _client.GetAsync("/RegisterKey/Verify/?identity=frodo.baggins.me");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    private void SeedDatabase(string identity)
    {
        var db = _factory.Services.GetRequiredService<KeyChainDatabase>();

        var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var ecc = new EccFullKeyData(pwd, 1);

        var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
        var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
        var r = new KeyChainRecord()
        {
            previousHash = hash,
            identity = identity,
            signedPreviousHash = key,
            algorithm = "ublah",
            publicKey = ecc.publicKey,
            recordHash = hash
        };
        db.tblKeyChain.Insert(r);
    }

    //
}

