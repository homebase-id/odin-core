using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.Core.Util;
using Odin.Notarius;
using Odin.NotariusTests.Integration;
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
    public async Task BeginRegistrationOnlyTest()
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit("frodobaggins.me");
        var (signedDocument, hashToSignBase64) = await BeginRegistrationTest(hobbit);
        signedDocument.VerifyEnvelopeSignatures();
    }

    [Test]
    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task BeginAndCompleteRegistrationTest()
    {
        var hobbit = HobbitSimulator.GetSimulatedHobbit("frodobaggins.me");
        var (signedDocument, hashToSignBase64) = await BeginRegistrationTest(hobbit);
        
        // We have a normally signed document and no notary public signature
        Assert.IsTrue(signedDocument.NotariusPublicus == null);

        var notarySignedDocument = await CompleteRegistrationTest(hobbit, signedDocument, hashToSignBase64);

        // Now we have a notarized document
        Assert.IsTrue(notarySignedDocument.NotariusPublicus != null);

        Assert.IsTrue(notarySignedDocument.VerifyEnvelopeSignatures());
        Assert.IsTrue(notarySignedDocument.VerifyNotariusPublicus());
    }

    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task<(SignedEnvelope, string)> BeginRegistrationTest(SimulatedHobbit hobbit)
    {
        var signedDocument = SimulatedHobbit.DocumentSignedEnvelope(new List<SimulatedHobbit> { hobbit });

        // Wrap the string inside a JSON object
        var postBody = new NotarizeBeginModel()
        {
            RequestorIdentity = hobbit.DomainName,
            SignedEnvelopeJson = signedDocument.GetCompactSortedJson()
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/Notarize/NotaryRegistrationBegin", postContent);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var hashToSignBase64 = await response.Content.ReadAsStringAsync();
        Debug.Assert(hashToSignBase64.Length > 1);

        byte[] previousHashToSign = Convert.FromBase64String(hashToSignBase64);

        // Assert
        Debug.Assert(previousHashToSign.Length >= 16);
        Debug.Assert(previousHashToSign.Length <= 32);

        return (signedDocument, hashToSignBase64);
    }

    // Test that we can successfully begin a key registration
    // And added an extra little test that we cannot send the same registration twice.
    public async Task<SignedEnvelope> CompleteRegistrationTest(SimulatedHobbit hobbit, SignedEnvelope signedDocument, string hashToSignbase64)
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

        Assert.NotNull(notarizedDocument);

        return notarizedDocument;
    }



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
            publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
            recordHash = hash
        };
        db.tblKeyChain.Insert(r);
    }

    //
}

