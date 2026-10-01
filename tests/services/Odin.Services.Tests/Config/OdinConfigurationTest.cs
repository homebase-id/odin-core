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

    // --- EmailSection (consolidated Email:* config; legacy top-level Mailgun:* fallback) ---









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


    // --- RelaySection (Email:Relay — outbound relay for tenant mail) ---

    [Test]
    public void RelaySection_DefaultsToNoRelay()
    {
        // The shipped default. Every host that has not opted in must behave exactly as before:
        // no onboarding, no extra DNS, no outbound API calls.
        var section = new OdinConfiguration.RelaySection(BuildConfig([]));

        Assert.That(section.Provider, Is.EqualTo(OdinConfiguration.RelayProvider.None));
        Assert.That(section.IsConfigured, Is.False);
        Assert.That(section.ApiKey, Is.Empty);
    }

    [Test]
    public void RelaySection_UnknownProvider_Throws()
    {
        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.RelaySection(BuildConfig(
            new Dictionary<string, string?> { ["Email:Relay:Provider"] = "Mailchimp" })));
    }

    [Test]
    public void RelaySection_Smtp2Go_RequiresApiKeyAndSmtpSettings()
    {
        // Fail at boot rather than at the first activation. A half-configured host would
        // otherwise hand a tenant a mailbox that cannot send, which is worse than not starting.
        Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.RelaySection(BuildConfig(
            new Dictionary<string, string?> { ["Email:Relay:Provider"] = "Smtp2Go" })));
    }

    [Test]
    public void RelaySection_Smtp2Go_ParsesAndDefaults()
    {
        var section = new OdinConfiguration.RelaySection(BuildConfig(new Dictionary<string, string?>
        {
            ["Email:Relay:Provider"] = "smtp2go", // case-insensitive on purpose
            ["Email:Relay:ApiKey"] = "api-xyz",
            ["Email:Relay:SmtpHost"] = "mail.smtp2go.com",
            ["Email:Relay:SmtpUsername"] = "homebase",
            ["Email:Relay:SmtpPassword"] = "secret",
        }));

        Assert.That(section.Provider, Is.EqualTo(OdinConfiguration.RelayProvider.Smtp2Go));
        Assert.That(section.IsConfigured, Is.True);
        Assert.That(section.ApiBaseUrl, Is.EqualTo("https://api.smtp2go.com/v3"));
        Assert.That(section.SmtpPort, Is.EqualTo(587));

        // Tracking rewrites recipients' links and adds a third per-tenant CNAME; it must never
        // switch itself on for an end-to-end encrypted mail product.
        Assert.That(section.EnableTracking, Is.False);
    }

    // --- StunSection (on by default; Enabled is the kill switch) ---

    [Test]
    public void StunSection_Defaults_WhenOmitted()
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(new Dictionary<string, string?>()));

        Assert.That(section.Enabled, Is.True, "the issue decided open by default with a kill switch");
        Assert.That(section.Port, Is.EqualTo(3478));
        Assert.That(section.BindAddress, Is.EqualTo("*"));
    }

    [Test]
    public void StunSection_Disabled_SkipsValidation()
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Stun:Enabled"] = "false",
            ["Stun:Port"] = "70000",
            ["Stun:BindAddress"] = "not-an-ip",
        }));

        Assert.That(section.Enabled, Is.False);
    }

    [Test]
    public void StunSection_Enabled_InvalidPort_Throws()
    {
        var ex = Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.StunSection(BuildConfig(
            new Dictionary<string, string?> { ["Stun:Port"] = "70000" })));

        Assert.That(ex!.Message, Does.Contain("Stun:Port"));
    }

    [Test]
    public void StunSection_Enabled_InvalidBindAddress_Throws()
    {
        var ex = Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.StunSection(BuildConfig(
            new Dictionary<string, string?> { ["Stun:BindAddress"] = "not-an-ip" })));

        Assert.That(ex!.Message, Does.Contain("Stun:BindAddress"));
    }

    [TestCase("127.0.0.1")]
    [TestCase("::1")]
    [TestCase("::")]
    public void StunSection_Enabled_LiteralBindAddress_Accepted(string bindAddress)
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Stun:BindAddress"] = bindAddress,
            ["Stun:Port"] = "0",
        }));

        Assert.That(section.GetBindAddress(), Is.EqualTo(System.Net.IPAddress.Parse(bindAddress)));
        Assert.That(section.Port, Is.Zero);
    }

    // --- Stun:PublicUrls (what clients put into iceServers) ---

    [Test]
    public void StunSection_PublicUrls_AbsentMeansEmpty_AndClientsGetTheIdentityName()
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Stun:Port"] = "3478",
        }));

        Assert.That(section.PublicUrls, Is.Empty);
        Assert.That(section.ClientUrls("frodo.example"), Is.EqualTo(new[] { "stun:frodo.example:3478" }),
            "a single host answers under its own name");
    }

    [Test]
    public void StunSection_PublicUrls_AreEchoedInOrder_WhateverEnabledSays()
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Stun:Enabled"] = "false",
            ["Stun:PublicUrls:0"] = "stun:stun1-1.eu.example:3478",
            ["Stun:PublicUrls:1"] = "stun:stun1-2.eu.example:3478",
            ["Stun:PublicUrls:2"] = "   ",
        }));

        var expected = new[] { "stun:stun1-1.eu.example:3478", "stun:stun1-2.eu.example:3478" };
        Assert.That(section.PublicUrls, Is.EqualTo(expected), "blank entries are dropped");
        Assert.That(section.ClientUrls("frodo.example"), Is.EqualTo(expected),
            "the list names whoever answers, so it wins even with this host's responder off");
    }

    [Test]
    public void StunSection_Disabled_WithoutPublicUrls_GivesClientsNothing()
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(new Dictionary<string, string?>
        {
            ["Stun:Enabled"] = "false",
        }));

        Assert.That(section.ClientUrls("frodo.example"), Is.Empty, "nothing answers, so the client gathers host candidates only");
    }

    [TestCase("https://stun1-1.eu.example:3478", Description = "wrong scheme")]
    [TestCase("stun1-1.eu.example:3478", Description = "no scheme")]
    [TestCase("stun:", Description = "no host")]
    [TestCase("stun:stun1-1.eu.example:99999", Description = "port out of range")]
    [TestCase("stun:stun1-1.eu.example:3478/x", Description = "path, not a host")]
    [TestCase("stun:-bad.example", Description = "not a domain name")]
    public void StunSection_PublicUrls_RejectsAnythingButAStunUrl(string entry)
    {
        var ex = Assert.Throws<OdinConfigException>(() => _ = new OdinConfiguration.StunSection(BuildConfig(
            new Dictionary<string, string?> { ["Stun:PublicUrls:0"] = entry })));

        Assert.That(ex!.Message, Does.Contain("Stun:PublicUrls").And.Contain(entry));
    }

    [TestCase("stun:stun1-1.eu.example")]
    [TestCase("stuns:stun1-1.eu.example:5349")]
    [TestCase("stun:192.0.2.10:3478")]
    [TestCase("stun:[2001:db8::1]:3478")]
    public void StunSection_PublicUrls_AcceptsRfc7064Forms(string entry)
    {
        var section = new OdinConfiguration.StunSection(BuildConfig(
            new Dictionary<string, string?> { ["Stun:PublicUrls:0"] = entry }));

        Assert.That(section.PublicUrls, Is.EqualTo(new[] { entry }));
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