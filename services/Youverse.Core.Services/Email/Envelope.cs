using System.Collections.Generic;

namespace Youverse.Core.Services.Email;

public class NameAndAddress
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Formatted => string.IsNullOrWhiteSpace(Name) ? $"<{Email}>" : $"{Name} <{Email}>";
}

public class Envelope
{
    public NameAndAddress From { get; set; } = new();
    public List<NameAndAddress> To { get; set; } = new();
    public List<NameAndAddress> Cc { get; set; } = new();
    public List<NameAndAddress> Bcc { get; set; } = new();
    public string Subject { get; set; } = "";
    public string TextMessage { get; set; } = "";
    public string HtmlMessage { get; set; } = "";
}