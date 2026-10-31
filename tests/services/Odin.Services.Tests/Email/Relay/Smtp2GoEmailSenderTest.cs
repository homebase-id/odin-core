#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Email;
using Odin.Services.Email.Relay;

namespace Odin.Services.Tests.Email.Relay;

/// <summary>
/// The host's own mail through SMTP2GO's /email/send: the same authenticated call as tenant
/// onboarding (<see cref="Smtp2GoApi"/>), From: the configured system sender.
/// </summary>
public class Smtp2GoEmailSenderTest
{
    private const string ApiKey = Smtp2GoTestRig.ApiKey;
    private const string SentBody = """{"request_id":"x","data":{"succeeded":1,"failed":0,"failures":[],"email_id":"1rCLaG-4hzZa6-Bp"}}""";

    private Smtp2GoTestRig _rig = null!;

    [Test]
    public async Task ItShouldSendFromTheSystemSenderWithTheKeyInTheHeader()
    {
        var sender = CreateSender(_ => (HttpStatusCode.OK, SentBody));

        await sender.SendAsync(new Envelope
        {
            To = [new NameAndEmailAddress { Name = "Doe, Jane", Email = "jane@example.org" }],
            Subject = "Please verify your new recovery email address!",
            TextMessage = "Open https://frodo.example.com/verify?id=abc",
            HtmlMessage = "<a href=\"https://frodo.example.com/verify?id=abc\">Verify</a>",
        });

        var request = _rig.Requests.Single();
        Assert.That(request.Path, Is.EqualTo("/v3/email/send"));
        Assert.That(request.ApiKeyHeader, Is.EqualTo(ApiKey));
        Assert.That(request.Body, Does.Not.Contain(ApiKey), "the key belongs in the header only");

        var body = JsonDocument.Parse(request.Body).RootElement;
        Assert.That(body.GetProperty("sender").GetString(), Is.EqualTo("\"Homebase\" <no-reply@ravenhosting.cloud>"), request.Body);
        Assert.That(body.GetProperty("to").EnumerateArray().Select(x => x.GetString()),
            Is.EqualTo(new[] { "\"Doe, Jane\" <jane@example.org>" }), request.Body);
        Assert.That(body.GetProperty("subject").GetString(), Is.EqualTo("Please verify your new recovery email address!"));
        Assert.That(body.GetProperty("text_body").GetString(), Does.Contain("https://frodo.example.com/verify?id=abc"));
        Assert.That(body.GetProperty("html_body").GetString(), Does.Contain("https://frodo.example.com/verify?id=abc"));
        Assert.That(body.TryGetProperty("cc", out _), Is.False, $"no cc was asked for: {request.Body}");
        Assert.That(body.TryGetProperty("bcc", out _), Is.False, $"no bcc was asked for: {request.Body}");
    }

    [Test]
    public void ItShouldReportARefusalWithItsStatusAndWithoutTheKey()
    {
        var sender = CreateSender(_ => (HttpStatusCode.BadRequest,
            """{"request_id":"x","data":{"error_code":"E_ApiResponseCodes.NON_VALIDATED_SENDER","error":"The sender is not verified"}}"""));

        var e = Assert.ThrowsAsync<MailRelayException>(() => sender.SendAsync(Mail()));

        Assert.That(e!.StatusCode, Is.EqualTo(400), $"status was {e.StatusCode}");
        Assert.That(e.Message, Does.Contain("/email/send").And.Contain("The sender is not verified"), e.Message);
        Assert.That(e.Message, Does.Not.Contain(ApiKey), e.Message);
    }

    /// <summary>A 200 can still refuse a recipient; that must not pass for a sent mail.</summary>
    [Test]
    public void ItShouldReportARefusedRecipientByCountOnly()
    {
        var sender = CreateSender(_ => (HttpStatusCode.OK,
            """{"request_id":"x","data":{"succeeded":0,"failed":1,"failures":["jane@example.org"]}}"""));

        var e = Assert.ThrowsAsync<EmailException>(() => sender.SendAsync(Mail()));

        Assert.That(e!.Message, Does.Contain("refused 1"), e.Message);
        Assert.That(e.Message, Does.Not.Contain("jane@example.org"), e.Message);
    }

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    public async Task ItShouldPassTheCheckOnlyWhenTheSenderDomainIsVerified(bool dkimVerified, bool rpathVerified, bool expected)
    {
        var sender = CreateSender(_ => (HttpStatusCode.OK, Smtp2GoTestRig.DomainsBody("ravenhosting.cloud", dkimVerified, rpathVerified)));

        var ok = await sender.VerifyCredentialsAsync();

        Assert.That(ok, Is.EqualTo(expected), $"dkim={dkimVerified} rpath={rpathVerified}");
        Assert.That(_rig.Requests.Single().Path, Is.EqualTo("/v3/domain/view"));
    }

    [Test]
    public async Task ItShouldFailTheCheckWhenTheSenderDomainIsNotOnTheAccount()
    {
        var sender = CreateSender(_ => (HttpStatusCode.OK, Smtp2GoTestRig.DomainsBody("other.example", true, true)));

        Assert.That(await sender.VerifyCredentialsAsync(), Is.False);
    }

    //

    private static Envelope Mail() => new()
    {
        To = [new NameAndEmailAddress { Email = "jane@example.org" }],
        Subject = "s",
        TextMessage = "t",
        HtmlMessage = "h",
    };

    private Smtp2GoEmailSender CreateSender(Func<string, (HttpStatusCode status, string body)> responder)
    {
        _rig = new Smtp2GoTestRig(responder, new NameAndEmailAddress { Name = "Homebase", Email = "no-reply@ravenhosting.cloud" });
        return _rig.Sender();
    }
}
