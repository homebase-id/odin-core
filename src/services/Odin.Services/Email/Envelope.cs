using System.Collections.Generic;

namespace Odin.Services.Email;

public class NameAndEmailAddress
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Formatted => string.IsNullOrWhiteSpace(Name) ? $"<{Email}>" : $"{Name} <{Email}>";
}

public class Envelope
{
    public NameAndEmailAddress From { get; set; } = new();
    public List<NameAndEmailAddress> To { get; set; } = new();
    public List<NameAndEmailAddress> Cc { get; set; } = new();
    public List<NameAndEmailAddress> Bcc { get; set; } = new();
    public string Subject { get; set; } = "";
    public string TextMessage { get; set; } = "";
    public string HtmlMessage { get; set; } = "";
}