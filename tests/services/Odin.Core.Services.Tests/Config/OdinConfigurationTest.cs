using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Email;

namespace Odin.Core.Services.Tests.Config;

public class OdinConfigurationTest
{
    [Test]
    public void MockTest()
    {
        var configMock = new OdinConfiguration
        {
            Mailgun = new OdinConfiguration.MailgunSection
            {
                EmailDomain = "example.com",
                DefaultFrom = new NameAndEmailAddress
                {
                    Email = "odin@middle.earth",
                    Name = "Odin Bossman"
                }
            }
        };

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(configMock);
        serviceCollection.AddSingleton<OdinConfigurationConsumer>();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var actualConfig = serviceProvider.GetRequiredService<OdinConfigurationConsumer>();
        actualConfig.Test();
    }

    private class OdinConfigurationConsumer
    {
        private readonly OdinConfiguration _config;

        public OdinConfigurationConsumer(OdinConfiguration config)
        {
            _config = config;
        }

        public void Test()
        {
            Assert.That(_config.Mailgun.EmailDomain, Is.EqualTo("example.com"));
            Assert.That(_config.Mailgun.DefaultFrom.Email, Is.EqualTo("odin@middle.earth"));
            Assert.That(_config.Mailgun.DefaultFrom.Name, Is.EqualTo("Odin Bossman"));
        }
    }

}