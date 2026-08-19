using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;

#nullable enable

namespace Odin.Services.Email;

public class SendGridSender : IEmailSender
{
    private readonly ILogger<SendGridSender> _logger;
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly NameAndEmailAddress _defaultFrom;

    public SendGridSender(
        ILogger<SendGridSender> logger,
        IDynamicHttpClientFactory httpClientFactory,
        string apiKey,
        NameAndEmailAddress defaultFrom)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _apiKey = apiKey;
        _defaultFrom = defaultFrom;
    }

    public async Task SendAsync(Envelope envelope)
    {
        var from = envelope.From.Formatted.Contains('@') ? envelope.From : _defaultFrom;

        var personalization = new Dictionary<string, object>
        {
            { "to", envelope.To.Select(Address).ToArray() }
        };
        if (envelope.Cc.Count > 0)
        {
            personalization["cc"] = envelope.Cc.Select(Address).ToArray();
        }
        if (envelope.Bcc.Count > 0)
        {
            personalization["bcc"] = envelope.Bcc.Select(Address).ToArray();
        }

        // SendGrid rejects empty content values; text/plain must come before text/html
        var content = new List<object>();
        if (!string.IsNullOrEmpty(envelope.TextMessage))
        {
            content.Add(new { type = "text/plain", value = envelope.TextMessage });
        }
        if (!string.IsNullOrEmpty(envelope.HtmlMessage))
        {
            content.Add(new { type = "text/html", value = envelope.HtmlMessage });
        }

        var body = new
        {
            personalizations = new[] { personalization },
            from = Address(from),
            subject = envelope.Subject,
            content
        };

        var httpClient = _httpClientFactory.CreateClient("api.sendgrid.com");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var result = await httpClient.PostAsJsonAsync("https://api.sendgrid.com/v3/mail/send", body);
        if (!result.IsSuccessStatusCode)
        {
            var reason = await result.Content.ReadAsStringAsync();
            throw new EmailException($"Error sending email. {reason}");
        }
    }

    public async Task<bool> VerifyCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient("api.sendgrid.com");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        var result = await httpClient.GetAsync("https://api.sendgrid.com/v3/scopes", cancellationToken);
        return result.IsSuccessStatusCode;
    }

    private static Dictionary<string, string> Address(NameAndEmailAddress address)
    {
        var result = new Dictionary<string, string> { { "email", address.Email } };
        if (!string.IsNullOrEmpty(address.Name))
        {
            result["name"] = address.Name;
        }
        return result;
    }
}
