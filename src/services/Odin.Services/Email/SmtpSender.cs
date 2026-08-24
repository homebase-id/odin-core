using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Email;

/// <summary>
/// Submits system mail into the host's own mail server over SMTP.
///
/// Homebase does not sign or relay outbound mail — it hands the message to the mail server, which
/// DKIM-signs it with the identity's provisioned key and relays it onward
/// (docs/email-keys-plan.md: "Homebase send API -> submits into Stalwart -> Stalwart DKIM-signs
/// -> relay"). Keeping signing on the mail server is what lets the DKIM private key stay out of
/// this process entirely.
///
/// The usual deployment submits over loopback to a mail server on the same host, so TLS is opt-in
/// rather than required: a self-signed dev certificate would otherwise fail every send, and
/// loopback has nothing to protect against.
/// </summary>
public class SmtpSender(
    ILogger<SmtpSender> logger,
    OdinConfiguration.SmtpProviderSection config,
    NameAndEmailAddress defaultFrom) : IEmailSender
{
    public async Task SendAsync(Envelope envelope)
    {
        var message = BuildMessage(envelope, defaultFrom);

        try
        {
            using var client = new SmtpClient();
            await ConnectAsync(client, CancellationToken.None);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }
        catch (Exception e)
        {
            // Same shape the other senders use: a send failure is an EmailException, so callers
            // do not have to know which provider is behind the interface.
            throw new EmailException($"SMTP submission to {config.RelayHost}:{config.RelayPort} failed", e);
        }

        logger.LogDebug("Submitted \"{subject}\" to {host}:{port}", envelope.Subject, config.RelayHost, config.RelayPort);
    }

    /// <summary>
    /// Connects and authenticates without sending, for the startup verifier. Cheap, and it
    /// catches the two things that actually go wrong: an unreachable relay and bad credentials.
    /// </summary>
    public async Task<bool> VerifyCredentialsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new SmtpClient();
            await ConnectAsync(client, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "SMTP relay {host}:{port} did not accept a connection", config.RelayHost, config.RelayPort);
            return false;
        }
    }

    private async Task ConnectAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        var security = config.RequireTls
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        if (!string.IsNullOrEmpty(config.LocalDomain))
        {
            // Otherwise MailKit greets with the OS hostname, which a mail server will reject if it
            // is not a FQDN ("5.5.0 Invalid EHLO domain") - and a Homebase host's OS hostname is
            // not its mail name in any case.
            client.LocalDomain = config.LocalDomain;
        }

        await client.ConnectAsync(config.RelayHost, config.RelayPort, security, cancellationToken);

        if (!string.IsNullOrEmpty(config.Username))
        {
            await client.AuthenticateAsync(config.Username, config.Password, cancellationToken);
        }
    }

    /// <summary>
    /// Envelope to MIME. Internal rather than private so the mapping can be tested without a
    /// mail server: the address handling and the text/html alternative are where this goes wrong.
    /// </summary>
    internal static MimeMessage BuildMessage(Envelope envelope, NameAndEmailAddress defaultFrom)
    {
        var message = new MimeMessage();

        // An envelope without a real From falls back to the configured system address, matching
        // MailgunSender - system mail should not be rejected for a missing sender.
        var from = string.IsNullOrWhiteSpace(envelope.From.Email) ? defaultFrom : envelope.From;
        message.From.Add(new MailboxAddress(from.Name ?? "", from.Email));

        foreach (var to in envelope.To)
        {
            message.To.Add(new MailboxAddress(to.Name ?? "", to.Email));
        }

        foreach (var cc in envelope.Cc)
        {
            message.Cc.Add(new MailboxAddress(cc.Name ?? "", cc.Email));
        }

        foreach (var bcc in envelope.Bcc)
        {
            message.Bcc.Add(new MailboxAddress(bcc.Name ?? "", bcc.Email));
        }

        message.Subject = envelope.Subject;

        var hasHtml = !string.IsNullOrEmpty(envelope.HtmlMessage);
        var hasText = !string.IsNullOrEmpty(envelope.TextMessage);

        if (hasHtml && hasText)
        {
            // multipart/alternative, plain text first: a reader that cannot render HTML shows the
            // text part, which is the whole point of sending both.
            var body = new MultipartAlternative
            {
                new TextPart(TextFormat.Plain) { Text = envelope.TextMessage },
                new TextPart(TextFormat.Html) { Text = envelope.HtmlMessage },
            };
            message.Body = body;
        }
        else if (hasHtml)
        {
            message.Body = new TextPart(TextFormat.Html) { Text = envelope.HtmlMessage };
        }
        else
        {
            message.Body = new TextPart(TextFormat.Plain) { Text = envelope.TextMessage };
        }

        return message;
    }
}
