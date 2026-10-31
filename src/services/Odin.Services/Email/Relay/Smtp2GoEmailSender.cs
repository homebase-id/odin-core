using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Util;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Email.Relay;

/// <summary>
/// The host's own mail (recovery links, risk reports, provisioning), sent through SMTP2GO's
/// /email/send on the same authenticated API as tenant onboarding. Always From:
/// Email:Relay:SystemFromEmail, since SMTP2GO refuses senders it has not verified.
/// </summary>
public class Smtp2GoEmailSender(
    OdinConfiguration configuration,
    Smtp2GoApi api,
    IMailRelayProvider relayProvider) : IEmailSender
{
    private NameAndEmailAddress SystemFrom => configuration.Email.Relay.SystemFrom;

    public async Task SendAsync(Envelope envelope)
    {
        var response = await api.PostAsync<Smtp2GoSendResponse>("/email/send", new Smtp2GoSendRequest
        {
            Sender = SystemFrom.Formatted,
            To = envelope.To.Select(x => x.Formatted).ToList(),
            Cc = NullIfEmpty(envelope.Cc),
            Bcc = NullIfEmpty(envelope.Bcc),
            Subject = envelope.Subject,
            TextBody = envelope.TextMessage,
            HtmlBody = envelope.HtmlMessage,
        }, CancellationToken.None);

        // Counts only: their failure entries name the recipients
        if (response.Data.Failed > 0 || response.Data.Succeeded == 0)
        {
            throw new EmailException(
                $"Relay: /email/send accepted {response.Data.Succeeded} recipient(s) and refused {response.Data.Failed}");
        }
    }

    /// <summary>
    /// The key works and the system From domain is a sender domain SMTP2GO has verified. Without
    /// the second, every send is refused ("MAIL FROM rejected" on EU1, 2026-10-31).
    /// </summary>
    public async Task<bool> VerifyCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var domain = SystemFrom.Email[(SystemFrom.Email.LastIndexOf('@') + 1)..];
        var state = await relayProvider.GetDomainAsync(new AsciiDomainName(domain), cancellationToken);
        return state?.Verified == true;
    }

    private static List<string>? NullIfEmpty(List<NameAndEmailAddress> addresses)
        => addresses.Count == 0 ? null : addresses.Select(x => x.Formatted).ToList();
}

/// <summary>Wire model for SMTP2GO's /email/send. Field names are theirs.</summary>
public class Smtp2GoSendRequest
{
    [JsonPropertyName("sender")] public string Sender { get; init; } = "";
    [JsonPropertyName("to")] public List<string> To { get; init; } = [];

    [JsonPropertyName("cc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Cc { get; init; }

    [JsonPropertyName("bcc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Bcc { get; init; }

    [JsonPropertyName("subject")] public string Subject { get; init; } = "";
    [JsonPropertyName("text_body")] public string TextBody { get; init; } = "";
    [JsonPropertyName("html_body")] public string HtmlBody { get; init; } = "";
}

public class Smtp2GoSendResponse
{
    [JsonPropertyName("request_id")] public string RequestId { get; init; } = "";
    [JsonPropertyName("data")] public Smtp2GoSendData Data { get; init; } = new();
}

public class Smtp2GoSendData
{
    [JsonPropertyName("succeeded")] public int Succeeded { get; init; }
    [JsonPropertyName("failed")] public int Failed { get; init; }
    [JsonPropertyName("email_id")] public string EmailId { get; init; } = "";
}
