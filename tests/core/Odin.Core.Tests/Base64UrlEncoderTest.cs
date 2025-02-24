using System;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Core.Tests;

public class Base64UrlEncoderTest
{
    [Test]
    public void ItShouldEncodeAndDecodeByteArray()
    {
        // Create a byte array by repeating the pattern that results in '+' and '/' in the Base64 output.
        // The length of the array is deliberately not a multiple of 3 to ensure padding.
        // (this almost brought chatgpt to its knees!)
        var input = new byte[] {
            0xFB, 0xFF, 0xFF, // This will encode to +/8
            0xFB, 0xFF, 0xFF, // Another +/8
            0xFB, 0xEF        // This will add a different pattern and ensure padding
        };

        // Test url base64 encode and decode
        var base64UrlEncoded = Base64UrlEncoder.Encode(input);
        var base64UrlDecoded = Base64UrlEncoder.Decode(base64UrlEncoded);
        CollectionAssert.AreEqual(input, base64UrlDecoded, "The two byte arrays should be equal");

        // Test normal base64 encode and decode
        var base64Encoded = Convert.ToBase64String(input);
        var base64Decoded = Convert.FromBase64String(base64Encoded);
        CollectionAssert.AreEqual(input, base64Decoded, "The two byte arrays should be equal");

        // Test that url-encoded can't be normal-decoded
        var exception = Assert.Throws<FormatException>(() => Convert.FromBase64String(base64UrlEncoded));
        Assert.That(exception.Message, Does.StartWith("The input is not a valid Base-64 string"));

        // Base64UrlEncoder.Decode can in some cases decode normal-encoded as well, but no guarantees (apparently)
        var sketchyBase64UrlDecoded = Base64UrlEncoder.Decode(base64Encoded);
        CollectionAssert.AreEqual(input, sketchyBase64UrlDecoded, "The two byte arrays should be equal");
    }

    [Test]
    public void ItShouldEncodeAndDecodeString()
    {
        const string input = "sdf j sdlfk jds fklj æøå";

        var base64UrlEncoded = Base64UrlEncoder.Encode(input);
        var base64UrlDecoded = Base64UrlEncoder.DecodeString(base64UrlEncoded);
        Assert.That(base64UrlDecoded, Is.EqualTo(input));
    }
}