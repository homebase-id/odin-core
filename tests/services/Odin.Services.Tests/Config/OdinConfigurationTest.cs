using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Configuration;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Services.Tests.Config;

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

    // Helper: build an IConfiguration from a flat dictionary of key=value pairs.
    private static IConfiguration BuildConfig(Dictionary<string, string?> pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

    // --- S3PayloadSection (independent toggle: S3Payload:Enabled, requires S3Storage:Enabled) ---

    [Test]
    public void S3PayloadSection_NotEnabled_ByDefault()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["S3Storage:Enabled"] = "true",
            // S3Payload:Enabled omitted -> defaults false (payload stays on disk)
        });

        var section = new OdinConfiguration.S3PayloadSection(config);

        Assert.That(section.Enabled, Is.False);
        Assert.That(section.BucketName, Is.EqualTo(""));
    }

    [Test]
    public void S3PayloadSection_Enabled_WithoutS3Storage_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["S3Payload:Enabled"] = "true",
            ["S3Storage:Enabled"] = "false",
            ["S3Payload:BucketName"] = "my-payload-bucket",
        });

        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.S3PayloadSection(config));
    }

    [Test]
    public void S3PayloadSection_Enabled_BucketMissing_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["S3Payload:Enabled"] = "true",
            ["S3Storage:Enabled"] = "true",
            // S3Payload:BucketName deliberately omitted
        });

        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.S3PayloadSection(config));
    }

    [Test]
    public void S3PayloadSection_Enabled_BucketPresent_EnabledTrueWithDefaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["S3Payload:Enabled"] = "true",
            ["S3Storage:Enabled"] = "true",
            ["S3Payload:BucketName"] = "my-payload-bucket",
        });

        var section = new OdinConfiguration.S3PayloadSection(config);

        Assert.That(section.Enabled, Is.True);
        Assert.That(section.BucketName, Is.EqualTo("my-payload-bucket"));
        Assert.That(section.RootPath, Is.EqualTo("payloads"));
    }

    [Test]
    public void S3PayloadSection_Enabled_CustomRootPath_IsHonored()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["S3Payload:Enabled"] = "true",
            ["S3Storage:Enabled"] = "true",
            ["S3Payload:BucketName"] = "my-payload-bucket",
            ["S3Payload:RootPath"] = "custom-payloads",
        });

        var section = new OdinConfiguration.S3PayloadSection(config);

        Assert.That(section.RootPath, Is.EqualTo("custom-payloads"));
    }

    [Test]
    public void S3StorageSection_Enabled_RetryDefaultsAndOverrides()
    {
        var def = new OdinConfiguration.S3StorageSection(BuildConfig(new Dictionary<string, string?>
        {
            ["S3Storage:Enabled"] = "true",
            ["S3Storage:AccessKey"] = "k",
            ["S3Storage:SecretAccessKey"] = "s",
            ["S3Storage:ServiceUrl"] = "https://example",
        }));
        Assert.That(def.RetryAttempts, Is.EqualTo(5));
        Assert.That(def.RetryInitialBackoffMs, Is.EqualTo(5000));

        var custom = new OdinConfiguration.S3StorageSection(BuildConfig(new Dictionary<string, string?>
        {
            ["S3Storage:Enabled"] = "true",
            ["S3Storage:AccessKey"] = "k",
            ["S3Storage:SecretAccessKey"] = "s",
            ["S3Storage:ServiceUrl"] = "https://example",
            ["S3Storage:RetryAttempts"] = "3",
            ["S3Storage:RetryInitialBackoffMs"] = "1000",
        }));
        Assert.That(custom.RetryAttempts, Is.EqualTo(3));
        Assert.That(custom.RetryInitialBackoffMs, Is.EqualTo(1000));
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