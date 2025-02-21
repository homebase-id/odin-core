using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Keychain;
using Odin.KeyChain;
using static Odin.Keychain.RegisterKeyController;

namespace Odin.KeyChainTests.Integration.Controllers;

public class RegisterKeyControllerTest
{
    private KeyChainWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private static SemaphoreSlim _uglyKludge = new SemaphoreSlim(1, 1);

    [SetUp]
    public void Setup()
    {
        if (_uglyKludge.Wait(TimeSpan.FromSeconds(100)) == false)
            throw new Exception("kaboom");
        _factory = new KeyChainWebApplicationFactory();
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
    public async Task BeginRegistrationTest()
    {
        var frodoDomain = new AsciiDomainName("frodobaggins.me");
        var frodo = HobbitSimulator.GetSimulatedHobbit(frodoDomain);
        var (previousHashBase64, signedInstruction) = await BeginRegistration(frodoDomain);
        var signedInstructionJson = signedInstruction.GetCompactSortedJson();

        //
        // Cheat and do an extra test that we cannot begin the same Instruction request twice
        //
        var postBody = new { signedRegistrationInstructionEnvelopeJson = signedInstructionJson };
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationBegin", postContent);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }


    [Test]
    // Test that we cannot begin if the public key doesn't match the signature public key
    public async Task BeginRegistrationWrongPublicKey()
    {
        var frodo = HobbitSimulator.GetSimulatedHobbit(new Core.Util.AsciiDomainName("frodobaggins.me"));
        // Arrange
        var signedInstruction = frodo.InstructionEnvelope();
        var signedInstructionJson = signedInstruction.GetCompactSortedJson();

        // Wrap the string inside a JSON object
        var postBody = new
        {
            signedRegistrationInstructionEnvelopeJson = signedInstructionJson
        };

        // Convert the object to a StringContent with the appropriate content type
        var postContent = new StringContent(JsonSerializer.Serialize(postBody), Encoding.UTF8, "application/json");

        var randomHobbit = HobbitSimulator.GetSimulatedHobbit(new Core.Util.AsciiDomainName("samwisegamgee.me"));
        var (p,e) = randomHobbit.GetKey();
        frodo.OverwriteKey(p, e);

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
    private async Task<(string, SignedEnvelope)> BeginRegistration(AsciiDomainName domain)
    {
        // Arrange
        var hobbit = HobbitSimulator.GetSimulatedHobbit(domain);
        var signedInstruction = hobbit.InstructionEnvelope();
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
    public async Task<HttpResponseMessage> FinalizeRegistration(AsciiDomainName domain, string previousHashBase64, string envelopeIdBase64)
    {
        //
        // Finalize
        //
        var hobbit = HobbitSimulator.GetSimulatedHobbit(domain);
        var signedPreviousHash = hobbit.SignPreviousHashForPublicKeyChain(previousHashBase64);
        var postBodyFinalize = new RegistrationCompleteModel() { EnvelopeIdBase64 = envelopeIdBase64, SignedPreviousHashBase64 = signedPreviousHash };
        var postContent = new StringContent(JsonSerializer.Serialize(postBodyFinalize), Encoding.UTF8, "application/json");

        return await _client.PostAsync("/RegisterKey/PublicKeyRegistrationComplete", postContent);
    }


    [Test]
    // TEST that we cannot begin a registration if we already have a recent public key record
    public async Task BeginFinalizeRegistrationTooManyRequests()
    {
        var frodoDomain = new AsciiDomainName("frodobaggins.me");
        var frodo = HobbitSimulator.GetSimulatedHobbit(frodoDomain);
        var (previousHashBase64, signedEnvelope) = await BeginRegistration(frodoDomain);

        // Finalize it
        var signedPreviousHash = frodo.SignPreviousHashForPublicKeyChain(previousHashBase64);
        var postBodyFinalize = new RegistrationCompleteModel() { EnvelopeIdBase64 = signedEnvelope.Envelope.ContentNonce.ToBase64(), SignedPreviousHashBase64 = signedPreviousHash };
        var postContent = new StringContent(JsonSerializer.Serialize(postBodyFinalize), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationComplete", postContent);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // Sneak in an extra test and make sure we cannot call it again
        //
        response = await _client.PostAsync("/RegisterKey/PublicKeyRegistrationComplete", postContent);
        content = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // Begin a new registration, also with Frodo, this will be denied because there must be
        // at least 30 days between registering a key in the database.

        // Arrange
        var signedInstruction = frodo.InstructionEnvelope();
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
        var frodoDomain = new AsciiDomainName("frodobaggins.me");
        var samwiseDomain = new AsciiDomainName("samwisegamgee.me");
        var gandalfDomain = new AsciiDomainName("gandalf.me");

        var frodo = HobbitSimulator.GetSimulatedHobbit(frodoDomain);
        var samwise = HobbitSimulator.GetSimulatedHobbit(samwiseDomain);
        var gandalf = HobbitSimulator.GetSimulatedHobbit(gandalfDomain);

        var (frodoPreviousHashBase64, frodoSignedEnvelope) = await BeginRegistration(frodoDomain);
        var (samPreviousHashBase64, samSignedEnvelope) = await BeginRegistration(samwiseDomain);
        var (gandalfPreviousHashBase64, gandalfSignedEnvelope) = await BeginRegistration(gandalfDomain);

        ClassicAssert.IsTrue(frodoPreviousHashBase64 == samPreviousHashBase64);
        ClassicAssert.IsTrue(frodoPreviousHashBase64 == gandalfPreviousHashBase64);
        ClassicAssert.IsTrue(frodoSignedEnvelope.Envelope.ContentNonce.ToBase64() != samSignedEnvelope.Envelope.ContentNonce.ToBase64());
        ClassicAssert.IsTrue(frodoSignedEnvelope.Envelope.ContentNonce.ToBase64() != gandalfSignedEnvelope.Envelope.ContentNonce.ToBase64());

        // We now have three simultaneous Begin requests all with the same previousHash

        // Let's finalize #2 first
        var r2 = await FinalizeRegistration(samwiseDomain, samPreviousHashBase64, samSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Let's finalize #1 but fail
        var r1 = await FinalizeRegistration(frodoDomain, frodoPreviousHashBase64, frodoSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r1.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        frodoPreviousHashBase64 = await r1.Content.ReadAsStringAsync(); // Get the new hash to sign
        ClassicAssert.IsTrue(frodoPreviousHashBase64 != samPreviousHashBase64);
        // Let's leave it non-finalized

        // Let's finailize #3 but fail
        var r3 = await FinalizeRegistration(gandalfDomain, gandalfPreviousHashBase64, gandalfSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r3.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        gandalfPreviousHashBase64 = await r3.Content.ReadAsStringAsync(); // Get the new hash to sign
        ClassicAssert.IsTrue(samPreviousHashBase64 != gandalfPreviousHashBase64);
        ClassicAssert.IsTrue(frodoPreviousHashBase64 == gandalfPreviousHashBase64);

        // Let's do it again for #3 to get it done
        r3 = await FinalizeRegistration(gandalfDomain, gandalfPreviousHashBase64, gandalfSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r3.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Now let's finally complete #1
        r1 = await FinalizeRegistration(frodoDomain, frodoPreviousHashBase64, frodoSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r1.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        frodoPreviousHashBase64 = await r1.Content.ReadAsStringAsync(); // Get the new hash to sign
        ClassicAssert.IsTrue(frodoPreviousHashBase64 != samPreviousHashBase64);
        ClassicAssert.IsTrue(frodoPreviousHashBase64 != gandalfPreviousHashBase64);

        // So do it again
        r1 = await FinalizeRegistration(frodoDomain, frodoPreviousHashBase64, frodoSignedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r1.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var db = _factory.Services.GetRequiredService<KeyChainDatabase>();
        using (var conn = db.CreateDisposableConnection())
        {
            ClassicAssert.IsTrue(await KeyChainDatabaseUtil.VerifyEntireBlockChainAsync(db, conn));
        }
    }


    [Test]
    public async Task GetVerifyShouldReturnIdentityAge()
    {
        // Create an entry
        var frodoDomain = new AsciiDomainName("frodobaggins.me");
        var (previousHashBase64, signedEnvelope) = await BeginRegistration(frodoDomain);
        var r = await FinalizeRegistration(frodoDomain, previousHashBase64, signedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Try to call verify
        var response = await _client.GetAsync($"/RegisterKey/Verify?identity={frodoDomain.DomainName}");
        var content = await response.Content.ReadAsStringAsync();
        var verifyResult = JsonSerializer.Deserialize<VerifyResult>(content);
        var delta = UnixTimeUtc.Now().seconds - verifyResult?.keyCreatedTime;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(delta, Is.GreaterThanOrEqualTo(0));
        Assert.That(delta, Is.LessThanOrEqualTo(5));
    }

    //

    [Test]
    public async Task GetVerifyShouldReturnNotFoundForUnknownIdentities()
    {
        // Create an entry for frodobaggins.me
        var frodoDomain = new AsciiDomainName("frodobaggins.me");
        var (previousHashBase64, signedEnvelope) = await BeginRegistration(frodoDomain);
        var r = await FinalizeRegistration(frodoDomain, previousHashBase64, signedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        var gandalfDomain = new AsciiDomainName("gandalf.me");
        var response = await _client.GetAsync($"/RegisterKey/Verify?identity={gandalfDomain.DomainName}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetVerifyKeyShouldReturnRange()
    {
        // Create an entry
        var frodoDomain = new AsciiDomainName("frodobaggins.me");
        var future1Domain = new AsciiDomainName("future1.me");
        var future2Domain = new AsciiDomainName("future2.me");

        var frodo = HobbitSimulator.GetSimulatedHobbit(frodoDomain);
        var future1 = HobbitSimulator.GetSimulatedHobbit(future1Domain);
        var future2 = HobbitSimulator.GetSimulatedHobbit(future2Domain);

        //
        // Entry one
        //
        LocalDateTime localDateTime = new LocalDateTime(2020, 1, 1, 11, 59);
        ZonedDateTime zonedDateTime = localDateTime.InZoneStrictly(DateTimeZone.Utc);
        Instant instant1 = zonedDateTime.ToInstant();
        RegisterKeyController.simulateTime = new UnixTimeUtc(instant1);

        var (previousHashBase64, signedEnvelope) = await BeginRegistration(frodoDomain);
        var r = await FinalizeRegistration(frodoDomain, previousHashBase64, signedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        EccPublicKeyData eccPublicKeyData1 = EccPublicKeyData.FromJwkBase64UrlPublicKey(signedEnvelope.Signatures[0].PublicKeyJwkBase64Url);

        //
        // Entry two
        //
        localDateTime = new LocalDateTime(2021, 1, 1, 11, 59);
        zonedDateTime = localDateTime.InZoneStrictly(DateTimeZone.Utc);
        var instant2 = zonedDateTime.ToInstant();
        RegisterKeyController.simulateTime = new UnixTimeUtc(instant2);

        // Replace Frodo's keys with new ones
        var (p, e) = future1.GetKey();
        frodo.OverwriteKey(p, e);

        (previousHashBase64, signedEnvelope) = await BeginRegistration(frodoDomain);
        r = await FinalizeRegistration(frodoDomain, previousHashBase64, signedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        EccPublicKeyData eccPublicKeyData2 = EccPublicKeyData.FromJwkBase64UrlPublicKey(signedEnvelope.Signatures[0].PublicKeyJwkBase64Url);


        //
        // Entry three, "now"
        //
        localDateTime = new LocalDateTime(2023, 1, 1, 11, 00);
        zonedDateTime = localDateTime.InZoneStrictly(DateTimeZone.Utc);
        var instant3 = zonedDateTime.ToInstant();
        RegisterKeyController.simulateTime = new UnixTimeUtc(instant3);

        (p, e) = future2.GetKey();
        frodo.OverwriteKey(p, e);

        (previousHashBase64, signedEnvelope) = await BeginRegistration(frodoDomain);
        r = await FinalizeRegistration(frodoDomain, previousHashBase64, signedEnvelope.Envelope.ContentNonce.ToBase64());
        Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        EccPublicKeyData eccPublicKeyData3 = EccPublicKeyData.FromJwkBase64UrlPublicKey(signedEnvelope.Signatures[0].PublicKeyJwkBase64Url);

        //
        // Now we're ready to test VerifyKey on the three entries
        //
        var response = await _client.GetAsync($"/RegisterKey/VerifyKey?identity={frodoDomain.DomainName}&PublicKeyJwkBase64Url={eccPublicKeyData1.PublicKeyJwkBase64Url()}");
        var content = await response.Content.ReadAsStringAsync();
        var verifyKeyResult = JsonSerializer.Deserialize<VerifyKeyResult>(content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        ClassicAssert.NotNull(verifyKeyResult);
        var tb = new UnixTimeUtc(instant1);
        var te = new UnixTimeUtc(instant2);
        Assert.That(verifyKeyResult.keyCreatedTime == tb.seconds);
        Assert.That(verifyKeyResult.successorKeyCreatedTime == te.seconds);


        // Test the second public key
        response = await _client.GetAsync($"/RegisterKey/VerifyKey?identity={frodoDomain.DomainName}&PublicKeyJwkBase64Url={eccPublicKeyData2.PublicKeyJwkBase64Url()}");
        content = await response.Content.ReadAsStringAsync();
        verifyKeyResult = JsonSerializer.Deserialize<VerifyKeyResult>(content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        ClassicAssert.NotNull(verifyKeyResult);
        tb = new UnixTimeUtc(instant2);
        te = new UnixTimeUtc(instant3);
        Assert.That(verifyKeyResult.keyCreatedTime == tb.seconds);
        Assert.That(verifyKeyResult.successorKeyCreatedTime == te.seconds);

        // Test the last public key
        response = await _client.GetAsync($"/RegisterKey/VerifyKey?identity={frodoDomain.DomainName}&PublicKeyJwkBase64Url={eccPublicKeyData3.PublicKeyJwkBase64Url()}");
        content = await response.Content.ReadAsStringAsync();
        verifyKeyResult = JsonSerializer.Deserialize<VerifyKeyResult>(content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        ClassicAssert.NotNull(verifyKeyResult);
        tb = new UnixTimeUtc(instant3);
        Assert.That(verifyKeyResult.keyCreatedTime == tb.seconds);
        ClassicAssert.IsNull(verifyKeyResult.successorKeyCreatedTime);

        RegisterKeyController.simulateTime = 0;
    }

    private void SeedDatabase(string identity)
    {
        var db = _factory.Services.GetRequiredService<KeyChainDatabase>();

        var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

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
        using (var myc = db.CreateDisposableConnection())
        {
            db.tblKeyChain.InsertAsync(myc, r).Wait();
        }
    }

    //
}

