using System.Collections.Generic;
using System.Linq;
using MimeKit;
using NUnit.Framework;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

/// <summary>
/// The Envelope-to-MIME mapping, which is where an SMTP sender actually goes wrong: a dropped
/// Bcc, a missing From, or an HTML-only body that leaves text-only readers with nothing.
/// Testable without a mail server because the mapping is separated from the transport.
/// </summary>
public class SmtpSenderTest
{
    private static readonly NameAndEmailAddress SystemFrom =
        new() { Name = "Homebase", Email = "no-reply@dotyou.cloud" };

    private static Envelope Envelope(
        string subject = "Your identity is ready",
        string text = "Hello",
        string html = "") => new()
    {
        From = new NameAndEmailAddress { Name = "Homebase", Email = "no-reply@dotyou.cloud" },
        To = [new NameAndEmailAddress { Name = "Frodo", Email = "mail@frodo.dotyou.cloud" }],
        Subject = subject,
        TextMessage = text,
        HtmlMessage = html,
    };

    [Test]
    public void ItCarriesEveryRecipientKind()
    {
        var envelope = Envelope();
        envelope.Cc = [new NameAndEmailAddress { Email = "sam@dotyou.cloud" }];
        envelope.Bcc = [new NameAndEmailAddress { Email = "audit@dotyou.cloud" }];

        var message = SmtpSender.BuildMessage(envelope, SystemFrom);

        Assert.That(Addresses(message.To), Is.EqualTo(new[] { "mail@frodo.dotyou.cloud" }));
        Assert.That(Addresses(message.Cc), Is.EqualTo(new[] { "sam@dotyou.cloud" }));
        Assert.That(Addresses(message.Bcc), Is.EqualTo(new[] { "audit@dotyou.cloud" }));
        Assert.That(message.Subject, Is.EqualTo("Your identity is ready"));
    }

    /// <summary>
    /// System mail with no sender falls back to the configured address rather than being sent
    /// From nobody, which receivers reject.
    /// </summary>
    [Test]
    public void AnEnvelopeWithoutAFromFallsBackToTheSystemAddress()
    {
        var envelope = Envelope();
        envelope.From = new NameAndEmailAddress();

        var message = SmtpSender.BuildMessage(envelope, SystemFrom);

        Assert.That(Addresses(message.From), Is.EqualTo(new[] { "no-reply@dotyou.cloud" }));
    }

    [Test]
    public void AnExplicitFromIsKept()
    {
        var envelope = Envelope();
        envelope.From = new NameAndEmailAddress { Name = "Frodo", Email = "mail@frodo.dotyou.cloud" };

        var message = SmtpSender.BuildMessage(envelope, SystemFrom);

        Assert.That(Addresses(message.From), Is.EqualTo(new[] { "mail@frodo.dotyou.cloud" }));
    }

    /// <summary>
    /// Both bodies means multipart/alternative with text FIRST — that ordering is what a
    /// text-only reader relies on to find something it can display.
    /// </summary>
    [Test]
    public void TextAndHtmlBecomeAlternativePartsWithTextFirst()
    {
        var message = SmtpSender.BuildMessage(Envelope(text: "plain", html: "<p>rich</p>"), SystemFrom);

        var alternative = message.Body as MultipartAlternative;
        Assert.That(alternative, Is.Not.Null, "both bodies should produce multipart/alternative");
        Assert.That(alternative!.Count, Is.EqualTo(2));
        Assert.That((alternative[0] as TextPart)!.IsPlain, Is.True, "plain text comes first");
        Assert.That((alternative[1] as TextPart)!.IsHtml, Is.True);
    }

    [Test]
    public void TextOnlyStaysASinglePlainPart()
    {
        var message = SmtpSender.BuildMessage(Envelope(text: "plain"), SystemFrom);

        var part = message.Body as TextPart;
        Assert.That(part, Is.Not.Null);
        Assert.That(part!.IsPlain, Is.True);
        Assert.That(part.Text, Is.EqualTo("plain"));
    }

    [Test]
    public void HtmlOnlyStaysASingleHtmlPart()
    {
        var message = SmtpSender.BuildMessage(Envelope(text: "", html: "<p>rich</p>"), SystemFrom);

        var part = message.Body as TextPart;
        Assert.That(part, Is.Not.Null);
        Assert.That(part!.IsHtml, Is.True);
    }

    private static IEnumerable<string> Addresses(InternetAddressList list) =>
        list.OfType<MailboxAddress>().Select(a => a.Address);
}
