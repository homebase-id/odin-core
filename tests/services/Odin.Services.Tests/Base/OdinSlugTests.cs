using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Base;

namespace Odin.Services.Tests.Base;

/// <summary>
/// The slug rule from docs/drive-addressing.md, <i>Slug format</i>. A slug is a URL path segment and a
/// wire address other identities resolve against, so anything needing percent-encoding, anything
/// readable as a path separator, and anything case-sensitive is rejected rather than coerced.
/// </summary>
[TestFixture]
public class OdinSlugTests
{
    [TestCase("chat")]
    [TestCase("public-posts")]
    [TestCase("a")]
    [TestCase("a1")]
    [TestCase("1")]
    [TestCase("acme-2")]
    [TestCase("abcdefghijkl")] // exactly MaxLength
    public void AcceptsAWellFormedSlug(string slug)
    {
        Assert.That(OdinSlug.IsValid(slug), Is.True, $"'{slug}' should be valid");
    }

    [TestCase("Chat", Description = "uppercase would make the address case-sensitive")]
    [TestCase("my chat", Description = "a space needs encoding")]
    [TestCase("my/chat", Description = "reads as a path separator")]
    [TestCase(".", Description = "reads as a path segment")]
    [TestCase("..", Description = "reads as a parent path segment")]
    [TestCase("my.chat")]
    [TestCase("my%chat")]
    [TestCase("my#chat")]
    [TestCase("my@chat")]
    [TestCase("-chat", Description = "a leading hyphen")]
    [TestCase("chat-", Description = "a trailing hyphen")]
    [TestCase("abcdefghijklmno", Description = "one over MaxLength")]
    [TestCase("")]
    [TestCase(null)]
    public void RejectsAMalformedSlug(string slug)
    {
        Assert.That(OdinSlug.IsValid(slug), Is.False, $"'{slug}' should be invalid");
    }

    [Test]
    public void AssertValidOrNull_AllowsAnAbsentSlug()
    {
        // Not required yet: a drive with no slug is addressed by Guid, which is every drive today.
        Assert.DoesNotThrow(() => OdinSlug.AssertValidOrNull(null, "driveSlug"));
        Assert.DoesNotThrow(() => OdinSlug.AssertValidOrNull("", "driveSlug"));
    }

    [Test]
    public void AssertValidOrNull_RejectsRatherThanCoercing()
    {
        // The value ends up in other identities' URLs, so silently lowercasing "Chat" would hand the
        // caller an address they did not ask for.
        var ex = Assert.Throws<OdinClientException>(() => OdinSlug.AssertValidOrNull("Chat", "driveSlug"));

        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.ArgumentError));
        Assert.That(ex.Message, Does.Contain("driveSlug"));
    }
}
