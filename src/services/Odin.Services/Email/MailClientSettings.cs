using System.Collections.Generic;
using System.Linq;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// What someone types into a mail app: hostnames, ports, encryption and username.
///
/// One definition, consumed by BOTH the autoconfig XML that clients fetch automatically and
/// the app's setup screen that people read off manually. They described the same server in
/// two places before, which is the sort of duplication that stays correct right up until a
/// port changes.
///
/// Config-derived and cheap - no network, no per-tenant state - so it rides the mail status
/// call rather than needing one of its own.
/// </summary>
public class MailClientSettings
{
    /// <summary>IMAP host. Comes from Email:TenantMail:MxNodes, so it differs per deployment.</summary>
    public string IncomingHost { get; init; } = "";

    public int IncomingPort { get; init; } = ImapPort;

    /// <summary>"SSL" - implicit TLS, NOT STARTTLS. Clients that pick the wrong one fail to connect.</summary>
    public string IncomingSocketType { get; init; } = SocketTypeSsl;

    public string OutgoingHost { get; init; } = "";

    public int OutgoingPort { get; init; } = SubmissionPort;

    public string OutgoingSocketType { get; init; } = SocketTypeSsl;

    /// <summary>
    /// The FULL email address, not the local part. Both servers use the same address and the
    /// same app password - a detail people get wrong often enough to be worth stating.
    /// </summary>
    public string Username { get; init; } = "";

    /// <summary>IMAP over implicit TLS.</summary>
    public const int ImapPort = 993;

    /// <summary>Submission over implicit TLS. Deliberately not 587: our autoconfig advertises implicit.</summary>
    public const int SubmissionPort = 465;

    public const string SocketTypeSsl = "SSL";

    /// <summary>
    /// Null when the host publishes no mail hosts - i.e. tenant mail is off, or is configured
    /// without MxNodes. Callers show nothing rather than a half-filled form.
    /// </summary>
    public static MailClientSettings? For(IReadOnlyList<string> mailHosts, string username)
    {
        var host = mailHosts.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return new MailClientSettings
        {
            IncomingHost = host,
            OutgoingHost = host,
            Username = username,
        };
    }
}
