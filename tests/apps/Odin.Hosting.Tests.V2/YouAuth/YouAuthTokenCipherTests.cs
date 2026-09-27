#nullable enable
using System;
using System.Linq;
using CryptographicException = System.Security.Cryptography.CryptographicException;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Serilog.Events;
using static Odin.Hosting.Tests.V2.YouAuth.YouAuthFlow;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// Which cipher seals the token response at [140]. The relying party names it on the authorize
/// request at [030] with <c>cipher</c>: <c>aes-gcm</c> on request, <c>aes-cbc</c> by default, so
/// a client in the field that says nothing keeps getting the bytes it gets today. The response
/// echoes what sealed it. Step numbers in the names are the flow diagram's
/// (<c>docs/youauth-unified-authorization.md</c>).
/// </summary>
/// <remarks>
/// A throwaway domain with no metadata document is the relying party, so nothing but the cipher is
/// in play. The three ported CBC tests in <c>Ported/YouAuth/YouAuthIntegrationTests</c> send no
/// <c>cipher</c> and decrypt with CBC; they are the other half of the byte-for-byte guarantee here.
/// </remarks>
[TestFixture]
public class YouAuthTokenCipherTests : V2Fixture
{
    private const string Domain = "amazoom.org";
    private const string DomainCallback = $"https://{Domain}/callback";

    // A client access token on the wire: 16-byte id, 16-byte half key, 1-byte type.
    private const int TokenLength = 33;
    private const int SharedSecretLength = 16;
    private const int GcmTagLength = 16;

    // ---------------------------------------------------------------------------------------
    // The default is what clients in the field get today
    // ---------------------------------------------------------------------------------------

    [TestCase("")]
    [TestCase("aes-cbc")]
    public async Task YouAuth140_WithoutACipherTheTokenIsSealedWithCbcAsBefore(string cipher)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var keyPair = NewKeyPair();
        var request = DomainRequest(Domain, DomainCallback);
        request.PublicKey = keyPair.PublicKeyJwk;
        request.Cipher = cipher;

        var callback = await AuthorizeWithConsentAsync(Host, owner, request);
        var (token, exchangeSecret) = await ExchangeTokenAsync(Host, Identities.Frodo, keyPair, callback);

        Assert.That(token.Cipher, Is.EqualTo(YouAuthDefaults.CipherAesCbc), "the response says what sealed it, even when the client did not ask");

        var (secretCipher, secretIv, tokenCipher, tokenIv) = Bytes(token);
        Assert.That(secretIv, Has.Length.EqualTo(16));
        Assert.That(tokenIv, Has.Length.EqualTo(16));
        Assert.That(secretCipher, Has.Length.EqualTo(32), "CBC pads a 16-byte secret to two blocks");
        Assert.That(tokenCipher, Has.Length.EqualTo(48), "CBC pads a 33-byte token to three blocks");

        var sharedSecret = AesCbc.Decrypt(secretCipher, exchangeSecret, secretIv);
        Assert.That(sharedSecret, Has.Length.EqualTo(SharedSecretLength));
        AssertIsAToken(AesCbc.Decrypt(tokenCipher, exchangeSecret, tokenIv));
    }

    // ---------------------------------------------------------------------------------------
    // On request, GCM
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task YouAuth140_ACipherOfAesGcmSealsTheTokenWithGcm()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var keyPair = NewKeyPair();
        var request = DomainRequest(Domain, DomainCallback);
        request.PublicKey = keyPair.PublicKeyJwk;
        request.Cipher = YouAuthDefaults.CipherAesGcm;

        var callback = await AuthorizeWithConsentAsync(Host, owner, request);
        var (token, exchangeSecret) = await ExchangeTokenAsync(Host, Identities.Frodo, keyPair, callback);

        Assert.That(token.Cipher, Is.EqualTo(YouAuthDefaults.CipherAesGcm));

        var (secretCipher, secretIv, tokenCipher, tokenIv) = Bytes(token);
        Assert.That(secretIv, Has.Length.EqualTo(16), "the IV field carries 16 bytes; the nonce is the first 12");
        Assert.That(tokenIv, Has.Length.EqualTo(16));
        Assert.That(secretCipher, Has.Length.EqualTo(SharedSecretLength + GcmTagLength), "GCM: plaintext plus the 16-byte tag, no padding");
        Assert.That(tokenCipher, Has.Length.EqualTo(TokenLength + GcmTagLength));

        var sharedSecret = AesGcm.Decrypt(secretCipher, exchangeSecret, secretIv);
        Assert.That(sharedSecret, Has.Length.EqualTo(SharedSecretLength));
        AssertIsAToken(AesGcm.Decrypt(tokenCipher, exchangeSecret, tokenIv));

        Assert.That(() => AesCbc.Decrypt(tokenCipher, exchangeSecret, tokenIv), Throws.InstanceOf<CryptographicException>(),
            "a client that opens GCM bytes as CBC fails loudly rather than reading garbage");
    }

    [Test]
    public async Task YouAuth050_TheCipherSurvivesTheConsentRoundTrip()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var keyPair = NewKeyPair();
        var request = DomainRequest(Domain, DomainCallback);
        request.PublicKey = keyPair.PublicKeyJwk;
        request.Cipher = YouAuthDefaults.CipherAesGcm;

        var returnUrl = AssertRedirectsToConsent(await AuthorizeAsync(Host, owner, request));
        Assert.That(returnUrl.Cipher, Is.EqualTo(YouAuthDefaults.CipherAesGcm), "the consent page's return URL carries the request as validated, cipher included");
        Assert.That(returnUrl.ToQueryString(), Does.Contain($"{YouAuthDefaults.Cipher}={YouAuthDefaults.CipherAesGcm}"));

        await GiveConsentAsync(Host, owner, returnUrl);
        var callback = AssertRedirectsTo(await AuthorizeAsync(Host, owner, request), DomainCallback);
        var (token, _) = await ExchangeTokenAsync(Host, Identities.Frodo, keyPair, callback);

        Assert.That(token.Cipher, Is.EqualTo(YouAuthDefaults.CipherAesGcm), "what the client asked for at [030] is what sealed the token after the detour");
    }

    [Test]
    public void YouAuth030_ARequestWithoutACipherBuildsTheSameQueryStringAsToday()
    {
        var request = DomainRequest(Domain, DomainCallback);

        var queryString = request.ToQueryString();
        Assert.That(queryString, Does.Not.Contain(YouAuthDefaults.Cipher), $"a request that sent nothing round-trips through the consent detour byte for byte: {queryString}");
        Assert.That(YouAuthAuthorizeRequest.FromQueryString(queryString).Cipher, Is.Empty);
    }

    // ---------------------------------------------------------------------------------------
    // A value the identity does not know
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task YouAuth060_AnUnknownCipherIsSentBackWithAnError()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var request = DomainRequest(Domain, DomainCallback);
        request.Cipher = "rot13";
        request.State = "state-4711";

        var location = AssertRedirectsTo(await AuthorizeAsync(Host, owner, request), DomainCallback);
        Assert.That(location[YouAuthDefaults.Error], Is.EqualTo(YouAuthDefaults.ErrorInvalidRequest));
        Assert.That(location[YouAuthDefaults.ErrorDescription], Does.Contain(YouAuthDefaults.Cipher), "the description names the parameter");
        Assert.That(location[YouAuthDefaults.State], Is.EqualTo("state-4711"));
    }

    // ---------------------------------------------------------------------------------------
    // Evidence for retiring CBC one day
    // ---------------------------------------------------------------------------------------

    [TestCase("", "absent")]
    [TestCase("aes-gcm", "aes-gcm")]
    public async Task YouAuth030_EveryAuthorizeLogsTheCipherItWasAsked(string cipher, string expected)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var clientId = $"cipher-log-{expected}.org";
        var request = DomainRequest(clientId, $"https://{clientId}/callback");
        request.Cipher = cipher;

        AssertRedirectsToConsent(await AuthorizeAsync(Host, owner, request));

        // Serilog renders a string property in quotes; they are dropped so the assertion reads as the line does.
        var authorizeLines = Host.LogStore.GetLogEvents()[LogEventLevel.Debug]
            .Select(e => e.RenderMessage().Replace("\"", ""))
            .Where(m => m.Contains($"client_id={clientId}"))
            .ToList();
        Assert.That(authorizeLines, Is.Not.Empty, "the authorize Debug line names the client");
        Assert.That(authorizeLines, Has.Some.Contains($"cipher={expected}"), $"so CBC can be retired on evidence; saw: {string.Join(" | ", authorizeLines)}");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static (byte[] secretCipher, byte[] secretIv, byte[] tokenCipher, byte[] tokenIv) Bytes(YouAuthTokenResponse token)
    {
        Assert.That(token.Base64SharedSecretCipher, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Base64SharedSecretIv, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Base64ClientAuthTokenCipher, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Base64ClientAuthTokenIv, Is.Not.Null.And.Not.Empty);

        return (
            Convert.FromBase64String(token.Base64SharedSecretCipher!),
            Convert.FromBase64String(token.Base64SharedSecretIv!),
            Convert.FromBase64String(token.Base64ClientAuthTokenCipher!),
            Convert.FromBase64String(token.Base64ClientAuthTokenIv!));
    }

    private static void AssertIsAToken(byte[] plain)
    {
        Assert.That(plain, Has.Length.EqualTo(TokenLength), $"a client access token on the wire; got {plain.ToBase64()}");
        var token = ClientAuthenticationToken.FromPortableBytes(plain);
        Assert.That(token.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(token.ClientTokenType, Is.EqualTo(ClientTokenType.YouAuth), "a domain the owner is not connected to gets a YouAuth token");
    }
}
