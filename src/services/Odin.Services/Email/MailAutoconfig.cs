using System.Collections.Generic;
using System.Xml.Linq;

namespace Odin.Services.Email;

/// <summary>
/// Thunderbird-style autoconfig document (docs/email-keys-plan.md "Client access"),
/// served at /.well-known/autoconfig/mail/config-v1.1.xml. The mail hosts are the
/// tenant's MX nodes: the host group's mail-server nodes serve IMAP/submission for
/// every tenant of the group, mirroring the MX record set. Contains no secrets -
/// %EMAILADDRESS% is a client-side placeholder the mail client substitutes.
/// </summary>
public static class MailAutoconfig
{
    public static string BuildXml(string domain, IReadOnlyList<string> mailHosts)
    {
        var emailProvider = new XElement("emailProvider",
            new XAttribute("id", domain),
            new XElement("domain", domain),
            new XElement("displayName", domain));

        foreach (var host in mailHosts)
        {
            emailProvider.Add(new XElement("incomingServer",
                new XAttribute("type", "imap"),
                new XElement("hostname", host),
                new XElement("port", MailClientSettings.ImapPort),
                new XElement("socketType", MailClientSettings.SocketTypeSsl),
                new XElement("authentication", "password-cleartext"),
                new XElement("username", "%EMAILADDRESS%")));
        }

        foreach (var host in mailHosts)
        {
            emailProvider.Add(new XElement("outgoingServer",
                new XAttribute("type", "smtp"),
                new XElement("hostname", host),
                new XElement("port", MailClientSettings.SubmissionPort),
                new XElement("socketType", MailClientSettings.SocketTypeSsl),
                new XElement("authentication", "password-cleartext"),
                new XElement("username", "%EMAILADDRESS%")));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("clientConfig",
                new XAttribute("version", "1.1"),
                emailProvider));

        return document.Declaration + "\n" + document;
    }
}
