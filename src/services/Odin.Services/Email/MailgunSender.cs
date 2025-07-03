using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;

#nullable enable

namespace Odin.Services.Email;

public class MailgunSender : IEmailSender
{
    private readonly ILogger<MailgunSender> _logger;
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly string _authToken;
    private readonly string _emailDomain;
    private readonly NameAndEmailAddress _defaultFrom;

    public MailgunSender(
        ILogger<MailgunSender> logger, 
        IDynamicHttpClientFactory httpClientFactory,
        string apiKey,
        string emailDomain, 
        NameAndEmailAddress defaultFrom)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _emailDomain = emailDomain;
        _defaultFrom = defaultFrom;
        _authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"));
    }

    public async Task SendAsync(Envelope envelope)
    {
        var from = envelope.From.Formatted.Contains('@') ? envelope.From.Formatted : _defaultFrom.Formatted; 
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string> {
            { "from", from },
            { "to", string.Join(",", envelope.To.Select(x => x.Formatted)) },
            { "cc", string.Join(",", envelope.Cc.Select(x => x.Formatted)) },
            { "bcc", string.Join(",", envelope.Bcc.Select(x => x.Formatted)) },
            { "subject", envelope.Subject },
            { "text", envelope.TextMessage },
            { "html", envelope.HtmlMessage }
        });        
        
        var httpClient = _httpClientFactory.CreateClient("api.mailgun.net");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _authToken);
        
        var result = await httpClient.PostAsync($"https://api.mailgun.net/v3/{_emailDomain}/messages", formContent);
        if (!result.IsSuccessStatusCode)
        {
            var reason = await result.Content.ReadAsStringAsync();
            throw new EmailException($"Error sending email. {reason}");
        }
    }
}
