using System.Collections.Generic;
using MimeKit;

namespace Odin.Services.Email;

public class NameAndEmailAddress
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>"Name &lt;email&gt;", with the name quoted when it holds a comma or the like; the bare address without one.</summary>
    public string Formatted => string.IsNullOrWhiteSpace(Name) ? Email : new MailboxAddress(Name, Email).ToString();
}

public class Envelope
{
    public List<NameAndEmailAddress> To { get; set; } = new();
    public List<NameAndEmailAddress> Cc { get; set; } = new();
    public List<NameAndEmailAddress> Bcc { get; set; } = new();
    public string Subject { get; set; } = "";
    public string TextMessage { get; set; } = "";
    public string HtmlMessage { get; set; } = "";
}