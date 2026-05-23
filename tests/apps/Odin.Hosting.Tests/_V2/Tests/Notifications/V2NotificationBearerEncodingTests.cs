using System;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.UnifiedV2.Notifications;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests._V2.Tests.Notifications;

/// <summary>
/// Pure-function coverage for the bearer-subprotocol decode path used by the v2
/// cookie-less WebSocket route (<see cref="V2NotificationSocketController"/>).
///
/// Motivation: the Kotlin (KMP) desktop/native client builds the bearer as
/// <c>odin.bearer.{Convert.ToBase64String(token.ToPortableBytes())}</c> — i.e.
/// STANDARD base64 (alphabet '+' '/', '=' padding), NOT base64url. The browser
/// client sends base64url (the subprotocol token grammar forbids '/'). Both must
/// resolve to the identical <see cref="ClientAuthenticationToken"/> after the
/// controller normalizes with <see cref="V2NotificationSocketController.Base64UrlToBase64"/>.
///
/// These tests pin that contract so a future "fix" to the normalizer can't
/// silently break native clients (whose tokens contain '/'/'+' ~half the time).
/// </summary>
[TestFixture]
public class V2NotificationBearerEncodingTests
{
    private const string BearerProtocolPrefix = "odin.bearer.";

    // ClientAuthenticationToken.ToPortableBytes() == Id(16) + AccessTokenHalfKey(16) + type(1)
    private const int PortableTokenLength = 33;

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] BuildPortableToken(Random rng, byte tokenType)
    {
        var bytes = new byte[PortableTokenLength];
        rng.NextBytes(bytes);
        bytes[PortableTokenLength - 1] = tokenType; // last byte = ClientTokenType
        return bytes;
    }

    [Test]
    public void Base64UrlToBase64_IsIdentityOnAlreadyStandardBase64_For33ByteTokens()
    {
        // A 33-byte token base64-encodes to exactly 44 chars with no '=' padding,
        // so the normalizer must leave standard base64 untouched (no '-'/'_' to
        // swap, length already a multiple of 4). This is the property that lets
        // native standard-base64 bearers authenticate at all.
        var rng = new Random(1);
        for (var i = 0; i < 5000; i++)
        {
            var bytes = BuildPortableToken(rng, (byte)ClientTokenType.App);
            var std = Convert.ToBase64String(bytes);
            ClassicAssert.AreEqual(44, std.Length, "33-byte token must encode to 44 base64 chars");
            ClassicAssert.IsFalse(std.Contains('='), "33-byte token base64 must not be padded");

            ClassicAssert.AreEqual(std, V2NotificationSocketController.Base64UrlToBase64(std),
                $"Normalizer must be identity on standard base64 of a 33-byte token (input={std})");
        }
    }

    [Test]
    public void Bearer_StandardBase64_And_Base64Url_BothResolveToSameToken()
    {
        var rng = new Random(42);
        var sawSlash = 0;
        var sawPlus = 0;

        for (var i = 0; i < 20000; i++)
        {
            var bytes = BuildPortableToken(rng, (byte)ClientTokenType.App);

            var expectedId = new Guid(bytes.Take(16).ToArray());
            var expectedHalfKey = bytes.Skip(16).Take(16).ToArray();
            var expectedType = (ClientTokenType)bytes[32];

            // What the KMP desktop/native client puts after "odin.bearer." (standard base64).
            var nativeBearerValue = Convert.ToBase64String(bytes);
            // What the browser client puts after "odin.bearer." (base64url, no padding).
            var browserBearerValue = ToBase64Url(bytes);

            if (nativeBearerValue.Contains('/')) sawSlash++;
            if (nativeBearerValue.Contains('+')) sawPlus++;

            AssertBearerResolves($"{BearerProtocolPrefix}{nativeBearerValue}", expectedId, expectedHalfKey, expectedType,
                "native/standard-base64");
            AssertBearerResolves($"{BearerProtocolPrefix}{browserBearerValue}", expectedId, expectedHalfKey, expectedType,
                "browser/base64url");
        }

        // Guard: ensure the random corpus actually exercised the '+' and '/'
        // characters that distinguish standard base64 from base64url — otherwise
        // the test would be vacuously green.
        ClassicAssert.Greater(sawSlash, 0, "expected some tokens whose standard base64 contains '/'");
        ClassicAssert.Greater(sawPlus, 0, "expected some tokens whose standard base64 contains '+'");
    }

    private static void AssertBearerResolves(string requestedSubProtocol, Guid expectedId, byte[] expectedHalfKey,
        ClientTokenType expectedType, string label)
    {
        // Mirror the controller's two steps exactly:
        //   token64 = Base64UrlToBase64(value.Substring(prefix.Length));
        //   ClientAuthenticationToken.TryParse(token64, out var cat);
        var raw = requestedSubProtocol.Substring(BearerProtocolPrefix.Length);
        var token64 = V2NotificationSocketController.Base64UrlToBase64(raw);

        var ok = ClientAuthenticationToken.TryParse(token64, out var cat);
        ClassicAssert.IsTrue(ok, $"[{label}] TryParse must succeed for bearer={requestedSubProtocol}");
        ClassicAssert.AreEqual(expectedId, cat.Id, $"[{label}] token Id mismatch");
        ClassicAssert.AreEqual(expectedType, cat.ClientTokenType, $"[{label}] token type mismatch");
        CollectionAssert.AreEqual(expectedHalfKey, cat.AccessTokenHalfKey.GetKey(),
            $"[{label}] access-token half key mismatch");
    }
}
