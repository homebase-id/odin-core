using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

#nullable enable

public class SendGridSenderTest
{
    private readonly ILogger<SendGridSender> _logger = new Mock<ILogger<SendGridSender>>().Object;
    private readonly DynamicHttpClientFactory _httpClientFactory = new(new Mock<ILogger<DynamicHttpClientFactory>>().Object);
    private const string ApiKey = "your-sendgrid-api-key";

    //

    [Test, Explicit]
    public async Task ItShouldSendAnEmailUsingExplicitFromAddress()
    {
        var defaultFrom = new NameAndEmailAddress { Name = "Saruman", Email = "saruman@gmail.com" };
        var mailSender = new SendGridSender(_logger, _httpClientFactory, ApiKey, defaultFrom);
        var envelope = new Envelope
        {
            From = new NameAndEmailAddress
            {
                Name = "Merry",
                Email = "sebbarg+odintestmerry@gmail.com",
            },
            To = new List<NameAndEmailAddress>
            {
                new() { Name = "Frodo", Email = "sebbarg+odintestfrodo@gmail.com" },
            },
            Cc = new List<NameAndEmailAddress>
            {
                new() { Name = "Gandalf", Email = "sebbarg+odintestgandalf@gmail.com" },
            },
            Subject = $"The Shire, {DateTime.Now.ToString(CultureInfo.InvariantCulture)}",
            TextMessage = "GO GO GO ring bearers!",
            HtmlMessage = "<h1>GO GO GO ring bearers!</h1>"
        };

        await mailSender.SendAsync(envelope);
    }

    //

    [Test, Explicit]
    public async Task ItShouldSendAnEmailUsingDefaultFromAddress()
    {
        var defaultFrom = new NameAndEmailAddress { Name = "", Email = "no-reply@odin.earth" };
        var mailSender = new SendGridSender(_logger, _httpClientFactory, ApiKey, defaultFrom);
        var envelope = new Envelope
        {
            To = new List<NameAndEmailAddress>
            {
                new() { Name = "Frodo", Email = "sebbarg+odintestfrodo@gmail.com" },
            },
            Subject = $"The Shire, {DateTime.Now.ToString(CultureInfo.InvariantCulture)}",
            TextMessage = "GO GO GO ring bearers!",
            HtmlMessage = "<h1>GO GO GO ring bearers!</h1>"
        };

        await mailSender.SendAsync(envelope);
    }

    //

    [Test, Explicit]
    public void ItShouldThrowOnError()
    {
        var from = new NameAndEmailAddress { Name = "Saruman", Email = "saruman@gmail.com" };
        var mailSender = new SendGridSender(_logger, _httpClientFactory, ApiKey, from);
        var envelope = new Envelope();
        Assert.ThrowsAsync<EmailException>(async () => await mailSender.SendAsync(envelope));
    }
}
