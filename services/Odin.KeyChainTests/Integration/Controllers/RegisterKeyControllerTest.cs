using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.KeyChain;
using static Odin.Keychain.RegisterKeyController;

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
    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task BeginRegistrationTest()
    {
        var (previousHashBase64, signedInstruction) = await BeginRegistration();


        var signedInstructionJson = signedInstruction.GetCompactSortedJson();

        // Wrap the string inside a JSON object
        var postBody = new
        {
            signedRegistrationInstructionEnvelopeJson = signedInstructionJson
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationBegin", postContent);
        var content = await response.Content.ReadAsStringAsync();
        byte[] previousHash = Convert.FromBase64String(content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Debug.Assert(content.Length > 1);
        Debug.Assert(previousHash.Length >= 16);
        Debug.Assert(previousHash.Length <= 32);

        //
        // Cheat and do an extra test that we cannot begin the same request twice
        //

        response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationBegin", postContent);
        content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }


    [Test]
    // Test that we cannot begin if the public key doesn't match the signature public key
    public async Task BeginRegistrationWrongPublicKey()
    {
        // Arrange
        var signedInstruction = SimulateFrodo.InstructionEnvelope();
        var signedInstructionJson = signedInstruction.GetCompactSortedJson();

        // Wrap the string inside a JSON object
        var postBody = new
        {
            signedRegistrationInstructionEnvelopeJson = signedInstructionJson
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");

        SimulateFrodo.NewKey(); // Discard the old public key and override the public key with a new one

        // Act
        var response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationBegin", postContent);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Debug.Assert(content.Length > 1);
    }

    //
    // Test that Begin will fail if I already have a finished one
    //

    /// <summary>
    /// returns 
    /// </summary>
    /// <returns></returns>
    private async Task<(string, SignedEnvelope)> BeginRegistration()
    {
        // Arrange
        var signedInstruction = SimulateFrodo.InstructionEnvelope();
        var signedInstructionJson = signedInstruction.GetCompactSortedJson();

        // Wrap the string inside a JSON object
        var postBody = new
        {
            signedRegistrationInstructionEnvelopeJson = signedInstructionJson
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationBegin", postContent);
        var previousHashBase64 = await response.Content.ReadAsStringAsync();
        byte[] previousHash = Convert.FromBase64String(previousHashBase64);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Debug.Assert(previousHashBase64.Length > 1);
        Debug.Assert(previousHash.Length >= 16);
        Debug.Assert(previousHash.Length <= 32);

        return (previousHashBase64, signedInstruction);
    }


    // Test that we can successfully begin and then finalize a key registration
    public async Task<HttpResponseMessage> FinalizeRegistration(string previousHashBase64, string envelopeIdBase64)
    {
        //
        // Finalize
        //
        var signedPreviousHash = SimulateFrodo.SignPreviousHashForPublicKeyChain(previousHashBase64);
        var postBodyFinalize = new RegistrationFinalizeModel() { EnvelopeIdBase64 = envelopeIdBase64, SignedPreviousHashBase64 = signedPreviousHash };
        var postContent = new StringContent(JsonSerializer.Serialize(postBodyFinalize), Encoding.UTF8, "application/json");

        return await _client.PostAsync("/RegisterKey/PublicKeyRegistrationFinalize", postContent);
    }


    [Test]
    // TEST that we cannot begin a registration if we already have a recent public key record
    public async Task BeginFinalizeRegistrationTooManyRequests()
    {
        var (previousHashBase64, signedEnvelope) = await BeginRegistration();

        // Finalize it
        var signedPreviousHash = SimulateFrodo.SignPreviousHashForPublicKeyChain(previousHashBase64);
        var postBodyFinalize = new RegistrationFinalizeModel() { EnvelopeIdBase64 = signedEnvelope.Envelope.ContentNonce.ToBase64(), SignedPreviousHashBase64 = signedPreviousHash };
        var postContent = new StringContent(JsonSerializer.Serialize(postBodyFinalize), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationFinalize", postContent);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // Sneak in an extra test and make sure we cannot call it again
        //
        response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationFinalize", postContent);
        content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // Begin a new registration, also with Frodo, this will be denied because there must be
        // at least 30 days between registering a key in the database.

        // Arrange
        var signedInstruction = SimulateFrodo.InstructionEnvelope();
        var signedInstructionJson = signedInstruction.GetCompactSortedJson();

        // Wrap the string inside a JSON object
        var postBody = new
        {
            signedRegistrationInstructionEnvelopeJson = signedInstructionJson
        };

        // Convert the object to a StringContent with the appropriate content type
        postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");

        // Act
        response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationBegin", postContent);
        content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }


    [Test]
    // TEST that if the same hash is returned, and one is done before the other, then we'll get a new hash to sign
    // Do it with three so we're sure it'll work in "multiple rounds"
    public async Task RegistrationRaceKeySign()
    {
        var (onePreviousHashBase64, oneSignedEnvelope) = await BeginRegistration();
        var (p1, e1, i1) = SimulateFrodo.GetKey();
        SimulateFrodo.NewKey("samwisegamgee.me");
        var (twoPreviousHashBase64, twoSignedEnvelope) = await BeginRegistration();
        var (p2, e2, i2) = SimulateFrodo.GetKey();
        SimulateFrodo.NewKey("gandalf.me");
        var (threePreviousHashBase64, threeSignedEnvelope) = await BeginRegistration();
        var (p3, e3, i3) = SimulateFrodo.GetKey();
        Assert.IsTrue(onePreviousHashBase64 == twoPreviousHashBase64);
        Assert.IsTrue(onePreviousHashBase64 == threePreviousHashBase64);
        Assert.IsTrue(oneSignedEnvelope.Envelope.ContentNonce.ToBase64() != twoSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.IsTrue(oneSignedEnvelope.Envelope.ContentNonce.ToBase64() != threeSignedEnvelope.Envelope.ContentNonce.ToBase64());

        // We now have three simultaneous Begin requests all with the same previousHash

        // Let's finalize #2 first
        SimulateFrodo.SetKey(p2, e2, i2);
        var r2 = await FinalizeRegistration(twoPreviousHashBase64, twoSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Let's finalize #1 but fail
        SimulateFrodo.SetKey(p1, e1, i1);
        var r1 = await FinalizeRegistration(onePreviousHashBase64, oneSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r1.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        onePreviousHashBase64 = await r1.Content.ReadAsStringAsync(); // Get the new hash to sign
        Assert.IsTrue(onePreviousHashBase64 != twoPreviousHashBase64);
        // Let's leave it non-finalized

        // Let's finailize #3 but fail
        SimulateFrodo.SetKey(p3, e3, i3);
        var r3 = await FinalizeRegistration(threePreviousHashBase64, threeSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r3.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        threePreviousHashBase64 = await r3.Content.ReadAsStringAsync(); // Get the new hash to sign
        Assert.IsTrue(twoPreviousHashBase64 != threePreviousHashBase64);
        Assert.IsTrue(onePreviousHashBase64 == threePreviousHashBase64);

        // Let's do it again for #3 to get it done
        r3 = await FinalizeRegistration(threePreviousHashBase64, threeSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r3.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Now let's finally complete #1
        SimulateFrodo.SetKey(p1, e1, i1);
        r1 = await FinalizeRegistration(onePreviousHashBase64, oneSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r1.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        onePreviousHashBase64 = await r1.Content.ReadAsStringAsync(); // Get the new hash to sign
        Assert.IsTrue(onePreviousHashBase64 != twoPreviousHashBase64);
        Assert.IsTrue(onePreviousHashBase64 != threePreviousHashBase64);

        // So do it again
        r1 = await FinalizeRegistration(onePreviousHashBase64, oneSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r1.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // HOW CAN I GET _db @seb?: KeyChainDatabaseUtil.VerifyEntireBlockChain(); ?

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

