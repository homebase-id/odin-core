#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Exceptions;

namespace Odin.Core.Tests;

/// <summary>
/// <see cref="OdinSlug"/>: always lower case, and never in need of escaping (RFC 3986 §2.3 unreserved,
/// RFC 1123 §2.1 host name label).
/// </summary>
public class OdinSlugTests
{
    [TestCase("chat")]
    [TestCase("public-posts")]
    [TestCase("a")]
    [TestCase("a1")]
    [TestCase("1")]
    [TestCase("acme-2")]
    [TestCase("abcdefghijklmn")] // exactly MaxLength
    public void AWellFormedSlugIsKeptAsIs(string value)
    {
        Assert.That(new OdinSlug(value).Value, Is.EqualTo(value));
    }

    [TestCase("Chat", "chat")]
    [TestCase("PUBLIC-POSTS", "public-posts")]
    [TestCase("Acme-2", "acme-2")]
    public void ASlugIsAlwaysLowerCase(string value, string expected)
    {
        Assert.That(new OdinSlug(value).Value, Is.EqualTo(expected));
    }

    [TestCase("my chat", Description = "a space needs escaping")]
    [TestCase("my/chat", Description = "reads as a path separator")]
    [TestCase(".", Description = "a dot segment")]
    [TestCase("..", Description = "a dot segment")]
    [TestCase("my.chat")]
    [TestCase("my_chat", Description = "not a host name label character")]
    [TestCase("my~chat", Description = "not a host name label character")]
    [TestCase("my%chat")]
    [TestCase("my#chat")]
    [TestCase("my@chat")]
    [TestCase("my'chat", Description = "would end an HTML attribute")]
    [TestCase("my\"chat")]
    [TestCase("my<chat")]
    [TestCase("my&chat")]
    [TestCase("café", Description = "not ASCII")]
    [TestCase("-chat", Description = "a leading hyphen")]
    [TestCase("chat-", Description = "a trailing hyphen")]
    [TestCase("abcdefghijklmno", Description = "one over MaxLength")]
    [TestCase("")]
    public void AMalformedSlugIsRefusedNotChanged(string value)
    {
        var ex = Assert.Throws<OdinClientException>(() => OdinSlug.Create(value, "driveSlug"));
        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.ArgumentError));
        Assert.That(ex.Message, Does.Contain("driveSlug"));
        Assert.That(OdinSlug.IsValid(value), Is.False);
        Assert.That(OdinSlug.TryParse(value, null, out _), Is.False);
    }

    /// <summary>The point of the type: every valid slug reads the same escaped as unescaped.</summary>
    [Test]
    public void AValidSlugNeverNeedsEscaping()
    {
        var random = new Random(1901);
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789-";
        var tried = 0;
        foreach (var slug in Samples())
        {
            Assert.That(Uri.EscapeDataString(slug), Is.EqualTo(slug.Value), "a URI path segment or query value");
            Assert.That(WebUtility.UrlEncode(slug), Is.EqualTo(slug.Value), "a form value");
            Assert.That(WebUtility.HtmlEncode(slug), Is.EqualTo(slug.Value), "HTML text or an attribute");
            Assert.That(JsonSerializer.Serialize(slug.Value), Is.EqualTo($"\"{slug.Value}\""), "a JSON string");
            // A label, not a whole host name: an all-digit host name would read as an IPv4 address.
            Assert.That(Uri.CheckHostName(slug + ".example"), Is.EqualTo(UriHostNameType.Dns), "a host name label");
            tried++;
        }

        Assert.That(tried, Is.GreaterThan(1000));

        IEnumerable<OdinSlug> Samples()
        {
            for (var i = 0; i < 5000; i++)
            {
                var length = random.Next(1, OdinSlug.MaxLength + 1);
                var value = new char[length];
                for (var j = 0; j < length; j++)
                {
                    value[j] = chars[random.Next(chars.Length)];
                }

                if (OdinSlug.TryParse(new string(value), null, out var slug))
                {
                    yield return slug;
                }
            }
        }
    }

    [Test]
    public void EqualityLowercasesAStringFirst()
    {
        var slug = new OdinSlug("chat");

        Assert.That(slug == "Chat", Is.True);
        Assert.That("CHAT" == slug, Is.True);
        Assert.That(slug != "Chat", Is.False);
        Assert.That(slug.Equals("Chat"), Is.True);
        Assert.That(slug.Equals((object)"Chat"), Is.True);
        Assert.That(slug == new OdinSlug("CHAT"), Is.True);
        Assert.That(slug == "chats", Is.False);
        Assert.That(slug == (string?)null, Is.False);
        Assert.That(new HashSet<OdinSlug> { new("Chat") }.Contains(slug), Is.True);
    }

    [Test]
    public void JsonIsAPlainString()
    {
        Assert.That(JsonSerializer.Serialize(new OdinSlug("Chat")), Is.EqualTo("\"chat\""));
        Assert.That(JsonSerializer.Deserialize<OdinSlug>("\"Chat\""), Is.EqualTo(new OdinSlug("chat")));
        Assert.That(JsonSerializer.Deserialize<OdinSlug?>("null"), Is.Null);
        Assert.That(JsonSerializer.Deserialize<OdinSlug>("null").HasValue, Is.False, "a field left out");
        Assert.That(JsonSerializer.Serialize(default(OdinSlug)), Is.EqualTo("null"));

        var ex = Assert.Throws<OdinClientException>(() => JsonSerializer.Deserialize<OdinSlug>("\"my chat\""));
        Assert.That(ex!.ErrorCode, Is.EqualTo(OdinClientErrorCode.ArgumentError));
    }

    [Test]
    public void RequiredRefusesAMissingSlug()
    {
        Assert.That(OdinSlug.Required(new OdinSlug("chat"), "appSlug").Value, Is.EqualTo("chat"));

        foreach (var missing in new OdinSlug?[] { null, default(OdinSlug) })
        {
            var ex = Assert.Throws<OdinClientException>(() => OdinSlug.Required(missing, "appSlug"));
            Assert.That(ex!.Message, Is.EqualTo("appSlug is required"));
        }
    }

    [Test]
    public void RouteValuesParseAsSlugs()
    {
        Assert.That(OdinSlug.TryParse("Feed", null, out var slug), Is.True);
        Assert.That(slug.Value, Is.EqualTo("feed"));
        Assert.That(OdinSlug.Parse("Feed", null), Is.EqualTo(slug));
        Assert.That(OdinSlug.TryParse(null, null, out _), Is.False);
    }
}
