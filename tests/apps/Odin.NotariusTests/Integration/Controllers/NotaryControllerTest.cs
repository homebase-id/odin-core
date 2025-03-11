using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.SQLite.NotaryDatabase;
using Odin.Notarius;
using static Odin.Notarius.NotarizeController;

namespace Odin.NotariusTests.Integration.Controllers;

public class NotaryControllerTest
{
    private NotaryWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private static SemaphoreSlim _uglyKludge = new SemaphoreSlim(1, 1);

    [SetUp]
    public void Setup()
    {
        if (_uglyKludge.Wait(TimeSpan.FromSeconds(100)) == false)
            throw new Exception("kaboom");
        _factory = new NotaryWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose(); // we need this to correctly dispose of the key chain database
        _uglyKludge.Release();
        _client.Dispose();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _uglyKludge.Dispose();
    }

    [Test]
    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task VerifyNonExistingTest()
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit("frodobaggins.me");
        var signedDocument = SimulatedHobbit.DocumentSignedEnvelope(new List<SimulatedHobbit> { hobbit });

        // Convert the object to a StringContent with the appropriate content type
        var base64Content = new StringContent(signedDocument.Signatures[0].Signature.ToBase64(), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/Notarize/VerifyNotarizedDocument", base64Content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }


    [Test]
    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task BeginNotarizeOnlyTest()
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit("frodobaggins.me");
        var (signedDocument, hashToSignBase64) = await BeginNotarizeHelper(hobbit);
        signedDocument.VerifyEnvelopeSignatures();
    }

    [Test]
    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task FullNotarizeTest()
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit("frodobaggins.me");
        var (signedDocument, hashToSignBase64) = await BeginNotarizeHelper(hobbit);
        
        // We have a normally signed document and no notary public signature
        ClassicAssert.IsTrue(signedDocument.NotariusPublicus == null);

        var notarySignedDocument = await CompleteNotarizeHelper(hobbit, signedDocument, hashToSignBase64);
    }

    [Test]
    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task CannotDoubleNotarizeTest()
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit("frodobaggins.me");
        var (signedDocument, hashToSignBase64) = await BeginNotarizeHelper(hobbit, HttpStatusCode.OK);

        // We have a normally signed document and no notary public signature
        ClassicAssert.IsTrue(signedDocument.NotariusPublicus == null);

        var notarySignedDocument = await CompleteNotarizeHelper(hobbit, signedDocument, hashToSignBase64);

        // Now try again and fail
        var (notarizedDocument, hash2ToSignBase64) = await BeginNotarizeHelper(hobbit, notarySignedDocument, HttpStatusCode.BadRequest);
    }



    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    private async Task<(SignedEnvelope, string)> BeginNotarizeHelper(SimulatedHobbit hobbit, SignedEnvelope signedDocument, HttpStatusCode expectedResponse = HttpStatusCode.OK)
    {
        // Wrap the string inside a JSON object
        var postBody = new NotarizeBeginModel()
        {
            RequestorIdentity = hobbit.DomainName,
            SignedEnvelopeJson = signedDocument.GetCompactSortedJson()
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/Notarize/NotaryRegistrationBegin", postContent);
        Assert.That(response.StatusCode, Is.EqualTo(expectedResponse));

        string hashToSignBase64 = "";

        if (response.StatusCode == HttpStatusCode.OK)
        {
            hashToSignBase64 = await response.Content.ReadAsStringAsync();
            ClassicAssert.IsTrue(hashToSignBase64.Length > 1);

            byte[] previousHashToSign = Convert.FromBase64String(hashToSignBase64);

            // Assert
            ClassicAssert.IsTrue(previousHashToSign.Length >= 16);
            ClassicAssert.IsTrue(previousHashToSign.Length <= 32);
        }
        return (signedDocument, hashToSignBase64);
    }


    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    private async Task<(SignedEnvelope, string)> BeginNotarizeHelper(SimulatedHobbit hobbit, HttpStatusCode expectedResponse = HttpStatusCode.OK)
    {
        var signedDocument = SimulatedHobbit.DocumentSignedEnvelope(new List<SimulatedHobbit> { hobbit });
        return await BeginNotarizeHelper(hobbit, signedDocument, expectedResponse);
    }


    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    private async Task<SignedEnvelope> CompleteNotarizeHelper(SimulatedHobbit hobbit, SignedEnvelope signedDocument, string hashToSignbase64)
    {
        //  Now let's sign the envelope.
        var signatureBase64 = hobbit.SignPreviousHashForPublicKeyChainBase64(hashToSignbase64);

        // Wrap the string inside a JSON object
        var postBody = new NotarizeCompleteModel()
        {
            EnvelopeIdBase64 = signedDocument.Envelope.ContentNonce.ToBase64(),
            SignedPreviousHashBase64 = signatureBase64
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/Notarize/NotaryRegistrationComplete", postContent);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var resultString = await response.Content.ReadAsStringAsync();
        var notarizedDocument = JsonSerializer.Deserialize<SignedEnvelope>(resultString);

        ClassicAssert.NotNull(notarizedDocument);
        ClassicAssert.IsTrue(notarizedDocument.NotariusPublicus != null);
        ClassicAssert.IsTrue(notarizedDocument.VerifyEnvelopeSignatures());
        ClassicAssert.IsTrue(notarizedDocument.VerifyNotariusPublicus());

        return notarizedDocument;
    }



    private void SeedDatabase(string identity)
    {
        using var db = _factory.Services.GetRequiredService<NotaryDatabase>();

        var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

        var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
        var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
        var r = new NotaryChainRecord()
        {
            previousHash = hash,
            identity = identity,
            signedPreviousHash = key,
            algorithm = "ublah",
            publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
            recordHash = hash
        };
        using (var myc = db.CreateDisposableConnection())
        {
            db.tblNotaryChain.InsertAsync(myc, r).Wait();
        }
    }
}

