using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Youverse.Core.Services.Email;

namespace Youverse.Core.Services.Tests.Email;

#nullable enable

public partial class MailgunSenderTest
{
    private readonly ILogger<MailgunSender> _logger = new Mock<ILogger<MailgunSender>>().Object;
    private readonly HttpClientFactory _httpClientFactory = new HttpClientFactory();
    private const string ApiKey = "dabae6512f685d927bbab05dcc1db0a4-5d9bd83c-8511d3cf";
    private const string EmailDomain = "sandbox967e15d7fff949a289ff21761c9428cc.mailgun.org";
    
    //
    
    [Test, Explicit]
    public async Task ItShouldSendAnEmail()
    {
        var mailSender = new MailgunSender(_logger, _httpClientFactory, ApiKey, EmailDomain);
        var envelope = new Envelope
        {
            From = new NameAndAddress
            {
                Name = "Merry",
                Email = "sebbarg+odintestmerry@gmail.com",
            },
            To = new List<NameAndAddress>
            {
                new () { Name = "Frodo", Email = "sebbarg+odintestfrodo@gmail.com"},
                new () { Name = "Sam", Email = "sebbarg+odintestsam@gmail.com"},
            },
            Cc = new List<NameAndAddress>
            {
                new () { Name = "Gandalf", Email = "sebbarg+odintestgandalf@gmail.com"},
            },
            Bcc = new List<NameAndAddress>
            {
                new () { Name = "Sauron", Email = "sebbarg+odintestsauron@gmail.com"},
            },
            Subject = $"The Shire, {DateTime.Now.ToString(CultureInfo.InvariantCulture)}",
            TextMessage = "GO GO GO ring bearers!",
            HtmlMessage = HtmlMail
        };

        await mailSender.SendAsync(envelope);
    }
    
    //
    
    [Test, Explicit]
    public void ItShouldThrowOnError()
    {
        var mailSender = new MailgunSender(_logger, _httpClientFactory, ApiKey, EmailDomain);
        var envelope = new Envelope();
        Assert.ThrowsAsync<EmailException>(async () => await mailSender.SendAsync(envelope));
    }
    
}

#region HtmlMail
public partial class MailgunSenderTest
{
    private const string HtmlMail = @"
        <!DOCTYPE html>
        <html>
        <head>
          <title>HTML Email Template</title>
          <style>
            body {
                font-family: Arial, sans-serif;
              line-height: 1.5;
              color: #333333;
            }

            .container {
                max-width: 600px;
              margin: 0 auto;
              padding: 20px;
            }

            h1 {
                color: #0099cc;
            }

            p {
                margin-bottom: 20px;
            }

            .button {
                display: inline-block;
              padding: 10px 20px;
              background-color: #0099cc;
              color: #ffffff;
              text-decoration: none;
            }
          </style>
        </head>
        <body>
          <div class='container'>
            <h1>GO GO GO ring bearers!</h1>
            <p>Luckily nobody evil sees this message.</p>
            </div>
        </body>
        </html>
    ";
}
#endregion
