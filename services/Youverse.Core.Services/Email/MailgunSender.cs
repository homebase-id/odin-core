using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

#nullable enable

namespace Youverse.Core.Services.Email;

public class MailgunSender : IEmailSender
{
    private readonly ILogger<MailgunSender> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _authToken;
    private readonly string _emailDomain;

    public MailgunSender(
        ILogger<MailgunSender> logger, 
        IHttpClientFactory httpClientFactory, 
        string apiKey,
        string emailDomain)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _emailDomain = emailDomain;
        _authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"));
    }

    public async Task SendAsync(Envelope envelope)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string> {
            { "from", envelope.From.Formatted },
            { "to", string.Join(",", envelope.To.Select(x => x.Formatted)) },
            { "cc", string.Join(",", envelope.Cc.Select(x => x.Formatted)) },
            { "bcc", string.Join(",", envelope.Bcc.Select(x => x.Formatted)) },
            { "subject", envelope.Subject },
            { "text", envelope.TextMessage },
            { "html", envelope.HtmlMessage }
        });        
        
        var httpClient = _httpClientFactory.CreateClient<MailgunSender>();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _authToken);
        
        var result = await httpClient.PostAsync($"https://api.mailgun.net/v3/{_emailDomain}/messages", formContent);
        if (!result.IsSuccessStatusCode)
        {
            var reason = await result.Content.ReadAsStringAsync();
            throw new EmailException($"Error sending email. {reason}");
        }
    }
}
