using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

/// <summary>
/// End-to-end proof that <see cref="SmtpSender"/> actually delivers: submits a message into the
/// local dev Stalwart and reads it back out of the recipient's mailbox over JMAP.
///
/// [Explicit] because it needs the dev instance from docker/stalwart-dev/compose.yml running with
/// both identities' domains provisioned — see docs/stalwart-dev-setup.md. Run it with:
///
///   dotnet test --filter "FullyQualifiedName~SmtpSenderLiveTest"
///
/// The unit-level mapping is covered by SmtpSenderTest, which needs nothing running; this one
/// exists because "the MIME looks right" and "the mail arrived" are different claims, and only
/// the second one tells you the submission path works.
/// </summary>
[TestFixture]
[Explicit("Requires the local Stalwart dev instance (docs/stalwart-dev-setup.md)")]
public class SmtpSenderLiveTest
{
    private const string AdminUser = "admin";
    private const string AdminPassword = "devadminpass";
    private const string ManagementUrl = "http://localhost:9080";
    private const int SubmissionPort = 2525;

    private const string Sender = "frodo@frodo.dotyou.cloud";
    private const string Recipient = "samwise@samwise.dotyou.cloud";

    /// <summary>
    /// Delivery, proved twice over: the message is findable by subject, AND the recipient's inbox
    /// counts moved by exactly one. The count is what the app puts on screen and behind a badge,
    /// so "a message exists somewhere" is not the claim that matters — "the number the user sees
    /// went up" is.
    /// </summary>
    [Test]
    public async Task ItDeliversFromOneLocalIdentityToAnother()
    {
        var subject = $"odin smtp probe {Guid.NewGuid():N}";
        var before = await ReadInboxCountsAsync();

        var sender = new SmtpSender(
            NullLogger<SmtpSender>.Instance,
            new OdinConfiguration.SmtpProviderSection
            {
                RelayHost = "localhost",
                RelayPort = SubmissionPort,
                LocalDomain = "frodo.dotyou.cloud",
            },
            new NameAndEmailAddress { Name = "Homebase", Email = Sender });

        await sender.SendAsync(new Envelope
        {
            From = new NameAndEmailAddress { Name = "Frodo", Email = Sender },
            To = [new NameAndEmailAddress { Name = "Samwise", Email = Recipient }],
            Subject = subject,
            TextMessage = "Sent by odin-core through the local mail server.",
        });

        // Delivery is asynchronous on the server's side, so poll rather than assume.
        var arrived = await WaitForMessageAsync(subject, TimeSpan.FromSeconds(20));
        Assert.That(arrived, Is.True, $"'{subject}' never arrived in {Recipient}'s mailbox");

        var after = await ReadInboxCountsAsync();
        Assert.That(after.Total, Is.EqualTo(before.Total + 1), "the inbox total should move by exactly one");
        Assert.That(after.Unread, Is.EqualTo(before.Unread + 1), "a delivered message arrives unread");
    }

    /// <summary>
    /// The same read the app makes: Mailbox/get against the recipient's account, matched on role
    /// rather than display name.
    /// </summary>
    private static async Task<(int Total, int Unread)> ReadInboxCountsAsync()
    {
        var accountId = await RecipientAccountIdAsync();

        var response = await JmapAsync(new JsonObject
        {
            ["using"] = new JsonArray("urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail"),
            ["methodCalls"] = new JsonArray(new JsonArray(
                "Mailbox/get", new JsonObject { ["accountId"] = accountId }, "0")),
        });

        var mailboxes = response["methodResponses"]?[0]?[1]?["list"] as JsonArray;
        var inbox = mailboxes?.FirstOrDefault(m => m?["role"]?.GetValue<string>() == "inbox")
                    ?? throw new InvalidOperationException($"{Recipient} has no inbox");

        return (
            inbox["totalEmails"]!.GetValue<int>(),
            inbox["unreadEmails"]!.GetValue<int>());
    }

    [Test]
    public async Task VerifyCredentialsSucceedsAgainstTheLocalRelay()
    {
        var sender = new SmtpSender(
            NullLogger<SmtpSender>.Instance,
            new OdinConfiguration.SmtpProviderSection
            {
                RelayHost = "localhost", RelayPort = SubmissionPort, LocalDomain = "frodo.dotyou.cloud",
            },
            new NameAndEmailAddress { Email = Sender });

        Assert.That(await sender.VerifyCredentialsAsync(), Is.True);
    }

    /// <summary>An unreachable relay reports false rather than throwing at startup.</summary>
    [Test]
    public async Task VerifyCredentialsFailsClosedOnAnUnreachableRelay()
    {
        var sender = new SmtpSender(
            NullLogger<SmtpSender>.Instance,
            new OdinConfiguration.SmtpProviderSection { RelayHost = "localhost", RelayPort = 25252 },
            new NameAndEmailAddress { Email = Sender });

        Assert.That(await sender.VerifyCredentialsAsync(), Is.False);
    }

    //

    private static async Task<bool> WaitForMessageAsync(string subject, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var accountId = await RecipientAccountIdAsync();

        while (DateTime.UtcNow < deadline)
        {
            var response = await JmapAsync(new JsonObject
            {
                ["using"] = new JsonArray("urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail"),
                ["methodCalls"] = new JsonArray(new JsonArray(
                    "Email/query",
                    new JsonObject
                    {
                        ["accountId"] = accountId,
                        ["filter"] = new JsonObject { ["subject"] = subject },
                    },
                    "0")),
            });

            var ids = response["methodResponses"]?[0]?[1]?["ids"] as JsonArray;
            if (ids is { Count: > 0 })
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<string> RecipientAccountIdAsync()
    {
        var response = await JmapAsync(new JsonObject
        {
            ["using"] = new JsonArray("urn:ietf:params:jmap:core", "urn:stalwart:jmap"),
            ["methodCalls"] = new JsonArray(new JsonArray(
                "x:Account/get", new JsonObject { ["accountId"] = "d333333" }, "0")),
        });

        var accounts = response["methodResponses"]?[0]?[1]?["list"] as JsonArray;
        var account = accounts?.FirstOrDefault(a => a?["emailAddress"]?.GetValue<string>() == Recipient)
                      ?? throw new InvalidOperationException(
                          $"Stalwart has no account for {Recipient}; see docs/stalwart-dev-setup.md");

        return account["id"]!.GetValue<string>();
    }

    private static async Task<JsonNode> JmapAsync(JsonObject body)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{AdminUser}:{AdminPassword}")));

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{ManagementUrl}/jmap", content);
        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
