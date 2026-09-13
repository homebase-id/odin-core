using System.Linq;
using NUnit.Framework;
using Odin.Services.Dns.PowerDns;

namespace Odin.Services.Tests.Dns.PowerDnsRest;

// The PowerDNS API stores TXT content verbatim as zone-file syntax - these tests pin the
// quote/escape/chunk rules (docs/email-dns-plan.md; the 255-byte split bites RSA DKIM keys)
public class ToTxtContentTest
{
    [Test]
    public void ItShouldQuoteAShortValue()
    {
        Assert.That(PowerDnsRestClient.ToTxtContent("v=spf1 include:_spf.id.pub -all"),
            Is.EqualTo("\"v=spf1 include:_spf.id.pub -all\""));
    }

    [Test]
    public void ItShouldEscapeQuotesAndBackslashes()
    {
        Assert.That(PowerDnsRestClient.ToTxtContent("say \"hi\" c:\\temp"),
            Is.EqualTo("\"say \\\"hi\\\" c:\\\\temp\""));
    }

    [Test]
    public void ItShouldSplitLongValuesIntoAdjacentQuotedChunks()
    {
        var value = new string('a', 300); // e.g. an RSA-2048 DKIM public key
        var content = PowerDnsRestClient.ToTxtContent(value);

        var chunks = content.Split(' ');
        Assert.That(chunks.Length, Is.EqualTo(2));
        Assert.That(chunks.All(c => c.StartsWith('"') && c.EndsWith('"')), Is.True);
        // Reassembling the chunks yields the original value
        var reassembled = string.Concat(chunks.Select(c => c[1..^1]));
        Assert.That(reassembled, Is.EqualTo(value));
        // No chunk's unquoted payload exceeds 255
        Assert.That(chunks.All(c => c.Length - 2 <= 255), Is.True);
    }

    [Test]
    public void ItShouldNeverSplitBetweenABackslashAndItsEscapedCharacter()
    {
        // 254 'a's + '"' escapes to 254 'a's + '\' + '"' = 256 chars: a naive split at 255
        // would end chunk 1 with a dangling backslash
        var value = new string('a', 254) + "\"";
        var content = PowerDnsRestClient.ToTxtContent(value);

        var chunks = content.Split(' ');
        foreach (var chunk in chunks)
        {
            var payload = chunk[1..^1];
            var trailingBackslashes = payload.Length - payload.TrimEnd('\\').Length;
            Assert.That(trailingBackslashes % 2, Is.EqualTo(0),
                $"chunk ends with a dangling escape: {chunk}");
        }

        var reassembled = string.Concat(chunks.Select(c => c[1..^1]))
            .Replace("\\\\", "\0").Replace("\\\"", "\"").Replace("\0", "\\");
        Assert.That(reassembled, Is.EqualTo(value));
    }
}
