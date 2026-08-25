using System;
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
            Email = new OdinConfiguration.EmailSection
            {
                Mailgun = new OdinConfiguration.MailgunProviderSection
                {
                    EmailDomain = "example.com",
                },
                SystemFrom = new NameAndEmailAddress
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

    // --- EmailSection (consolidated Email:* config; legacy top-level Mailgun:* fallback) ---

    [Test]
    public void EmailSection_DefaultsToProviderNone()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig([]));

        Assert.That(section.Provider, Is.EqualTo(EmailProvider.None));
        Assert.That(section.IsProviderConfigured, Is.False);
        Assert.That(section.UsingDeprecatedMailgunSection, Is.False);
        Assert.That(section.TenantMail.Enabled, Is.False);
    }

    [Test]
    public void EmailSection_ParsesSendGridProvider()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "SendGrid",
            ["Email:SendGrid:ApiKey"] = "sg-key",
            ["Email:SystemFrom:Email"] = "no-reply@id.pub",
            ["Email:SystemFrom:Name"] = "Team Homebase",
        }));

        Assert.That(section.Provider, Is.EqualTo(EmailProvider.SendGrid));
        Assert.That(section.IsProviderConfigured, Is.True);
        Assert.That(section.SendGrid.ApiKey, Is.EqualTo("sg-key"));
        Assert.That(section.SystemFrom.Email, Is.EqualTo("no-reply@id.pub"));
        Assert.That(section.SystemFrom.Name, Is.EqualTo("Team Homebase"));
    }

    [Test]
    public void EmailSection_SelectedProviderCredentialsAreRequired()
    {
        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.EmailSection(BuildConfig(
            new Dictionary<string, string?>
            {
                ["Email:Provider"] = "SendGrid",
                ["Email:SystemFrom:Email"] = "no-reply@id.pub",
                // Email:SendGrid:ApiKey deliberately omitted
            })));

        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.EmailSection(BuildConfig(
            new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Mailgun",
                ["Email:SystemFrom:Email"] = "no-reply@id.pub",
                ["Email:Mailgun:ApiKey"] = "mg-key",
                // Email:Mailgun:EmailDomain deliberately omitted
            })));
    }

    [Test]
    public void EmailSection_UnselectedProviderCredentialsAreNotRequired()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Mailgun",
            ["Email:Mailgun:ApiKey"] = "mg-key",
            ["Email:Mailgun:EmailDomain"] = "mg.example.com",
            ["Email:SystemFrom:Email"] = "no-reply@id.pub",
            // no SendGrid/Smtp keys
        }));

        Assert.That(section.Provider, Is.EqualTo(EmailProvider.Mailgun));
        Assert.That(section.Mailgun.EmailDomain, Is.EqualTo("mg.example.com"));
        Assert.That(section.SendGrid.ApiKey, Is.EqualTo(""));
    }

    [Test]
    public void EmailSection_DeprecatedMailgunSection_MapsToMailgunProvider()
    {
        // Old deployments carry only top-level Mailgun:* keys (no Email section)
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Mailgun:Enabled"] = "true",
            ["Mailgun:ApiKey"] = "legacy-key",
            ["Mailgun:EmailDomain"] = "legacy.example.com",
            ["Mailgun:DefaultFromEmail"] = "no-reply@legacy.example.com",
            ["Mailgun:DefaultFromName"] = "Legacy Sender",
        }));

        Assert.That(section.UsingDeprecatedMailgunSection, Is.True);
        Assert.That(section.Provider, Is.EqualTo(EmailProvider.Mailgun));
        Assert.That(section.IsProviderConfigured, Is.True);
        Assert.That(section.Mailgun.ApiKey, Is.EqualTo("legacy-key"));
        Assert.That(section.Mailgun.EmailDomain, Is.EqualTo("legacy.example.com"));
        Assert.That(section.SystemFrom.Email, Is.EqualTo("no-reply@legacy.example.com"));
        Assert.That(section.SystemFrom.Name, Is.EqualTo("Legacy Sender"));
        Assert.That(section.TenantMail.Enabled, Is.False);
    }

    [Test]
    public void EmailSection_LegacyMailgunDisabled_MapsToProviderNone()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Mailgun:Enabled"] = "false",
        }));

        Assert.That(section.UsingDeprecatedMailgunSection, Is.True);
        Assert.That(section.Provider, Is.EqualTo(EmailProvider.None));
        Assert.That(section.IsProviderConfigured, Is.False);
    }

    /// <summary>
    /// The system sender and tenant mail are independent settings, and enabling one must not
    /// disable the other. They used to be coupled: the legacy branch early-returned whenever an
    /// Email section existed, so adding Email:TenantMail:* to a host still using the deprecated
    /// Mailgun block silently stopped all system mail - no error, no bounce, just no password
    /// recovery. Migrating Mailgun was therefore an undocumented prerequisite for tenant mail.
    /// </summary>
    [Test]
    public void EmailSection_TenantMail_DoesNotDisableLegacyMailgun()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            // Tenant mail configured in the new section...
            ["Email:TenantMail:Enabled"] = "true",
            ["Email:TenantMail:MxNodes:0"] = "mx1.example.com",
            ["Email:TenantMail:SpfIncludeTarget"] = "spf.example.com",
            ["Email:TenantMail:DmarcReportEmail"] = "dmarc@example.com",
            ["Email:TenantMail:TlsReportEmail"] = "tls@example.com",
            // ...while the system sender is still the deprecated top-level block
            ["Mailgun:Enabled"] = "true",
            ["Mailgun:ApiKey"] = "legacy-key",
            ["Mailgun:EmailDomain"] = "legacy.example.com",
            ["Mailgun:DefaultFromEmail"] = "no-reply@legacy.example.com",
        }));

        Assert.That(section.TenantMail.Enabled, Is.True);
        Assert.That(section.Provider, Is.EqualTo(EmailProvider.Mailgun), "legacy system mail must survive");
        Assert.That(section.Mailgun.ApiKey, Is.EqualTo("legacy-key"));
        Assert.That(section.SystemFrom.Email, Is.EqualTo("no-reply@legacy.example.com"));
        Assert.That(section.UsingDeprecatedMailgunSection, Is.True);
    }

    [Test]
    public void EmailSection_EmailSectionWins_OverLegacyMailgun()
    {
        // During migration both may exist; the new section takes precedence
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "None",
            ["Mailgun:Enabled"] = "true",
            ["Mailgun:ApiKey"] = "legacy-key",
            ["Mailgun:EmailDomain"] = "legacy.example.com",
            ["Mailgun:DefaultFromEmail"] = "no-reply@legacy.example.com",
        }));

        Assert.That(section.UsingDeprecatedMailgunSection, Is.False);
        Assert.That(section.Provider, Is.EqualTo(EmailProvider.None));
    }

    [Test]
    public void EmailSection_ParsesTenantMail()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "SendGrid",
            ["Email:SendGrid:ApiKey"] = "sg-key",
            ["Email:SystemFrom:Email"] = "no-reply@id.pub",
            ["Email:TenantMail:Enabled"] = "true",
            ["Email:TenantMail:CanaryDomain"] = "canary.id.pub",
            ["Email:TenantMail:MxNodes:0"] = "node-a.example.com",
            ["Email:TenantMail:MxNodes:1"] = "node-b.example.com",
            ["Email:TenantMail:SpfIncludeTarget"] = "_spf.id.pub",
            ["Email:TenantMail:DmarcReportEmail"] = "dmarc-reports@id.pub",
            ["Email:TenantMail:TlsReportEmail"] = "tls-reports@id.pub",
        }));

        Assert.That(section.TenantMail.Enabled, Is.True);
        Assert.That(section.TenantMail.CanaryDomain, Is.EqualTo("canary.id.pub"));
        Assert.That(section.TenantMail.MxNodes, Is.EqualTo(new List<string> { "node-a.example.com", "node-b.example.com" }));
        Assert.That(section.TenantMail.SpfIncludeTarget, Is.EqualTo("_spf.id.pub"));
        Assert.That(section.TenantMail.DmarcReportEmail, Is.EqualTo("dmarc-reports@id.pub"));
        Assert.That(section.TenantMail.TlsReportEmail, Is.EqualTo("tls-reports@id.pub"));
    }

    [Test]
    public void EmailSection_TenantMailEnabled_RequiresDnsValues()
    {
        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.EmailSection(BuildConfig(
            new Dictionary<string, string?>
            {
                ["Email:Provider"] = "SendGrid",
                ["Email:SendGrid:ApiKey"] = "sg-key",
                ["Email:SystemFrom:Email"] = "no-reply@id.pub",
                ["Email:TenantMail:Enabled"] = "true",
                // MxNodes etc. deliberately omitted
            })));
    }

    [Test]
    public void EmailSection_DkimStorageKey_DefaultsToEmpty()
    {
        var section = new OdinConfiguration.EmailSection(BuildConfig([]));
        Assert.That(section.DkimStorageKey, Is.Empty);
    }

    [Test]
    public void EmailSection_DkimStorageKey_ParsesThirtyTwoByteHex()
    {
        var hex = new string('A', 64);
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Email:DkimStorageKey"] = hex,
        }));

        Assert.That(section.DkimStorageKey, Is.EqualTo(Convert.FromHexString(hex)));
    }

    [Test]
    public void EmailSection_DkimStorageKey_RejectsWrongLength()
    {
        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.EmailSection(BuildConfig(
            new Dictionary<string, string?>
            {
                ["Email:DkimStorageKey"] = "DECAFBAD",
            })));
    }

    [Test]
    public void EmailSection_DkimStorageKey_IsEmptyWhenUsingDeprecatedMailgunSection()
    {
        // The legacy branch early-returns; the key must simply stay empty, not throw
        var section = new OdinConfiguration.EmailSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Mailgun:Enabled"] = "false",
        }));

        Assert.That(section.UsingDeprecatedMailgunSection, Is.True);
        Assert.That(section.DkimStorageKey, Is.Empty);
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
            Assert.That(_config.Email.Mailgun.EmailDomain, Is.EqualTo("example.com"));
            Assert.That(_config.Email.SystemFrom.Email, Is.EqualTo("odin@middle.earth"));
            Assert.That(_config.Email.SystemFrom.Name, Is.EqualTo("Odin Bossman"));
        }
    }

}