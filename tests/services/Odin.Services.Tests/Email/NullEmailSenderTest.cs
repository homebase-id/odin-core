using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

#nullable enable

public class NullEmailSenderTest
{
    [Test]
    public async Task ItShouldLogAndDiscard()
    {
        var logger = new Mock<ILogger<NullEmailSender>>();
        var sender = new NullEmailSender(logger.Object);

        await sender.SendAsync(new Envelope
        {
            To = new List<NameAndEmailAddress> { new() { Name = "Frodo", Email = "frodo@shire.example" } },
            Subject = "The Shire",
            TextMessage = "GO GO GO ring bearers!",
        });

        logger.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("frodo@shire.example")),
            null,
            It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()), Times.Once);
    }
}
