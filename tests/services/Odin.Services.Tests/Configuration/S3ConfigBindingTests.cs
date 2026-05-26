using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Odin.Services.Configuration;

namespace Odin.Services.Tests.Configuration;

public class S3ConfigBindingTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Test]
    public void SharedConnection_And_PerUseSections_BindFromConfig()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["S3Storage:AccessKey"] = "ak",
            ["S3Storage:SecretAccessKey"] = "sk",
            ["S3Storage:ServiceUrl"] = "https://s3.example.com",
            ["S3Storage:Region"] = "eu-1",
            ["S3Storage:ForcePathStyle"] = "true",

            ["S3PayloadStorage:Enabled"] = "true",
            ["S3PayloadStorage:BucketName"] = "payload-bucket",

            ["S3InboxStorage:Enabled"] = "true",
            ["S3InboxStorage:BucketName"] = "inbox-bucket",
        });

        var s3 = new OdinConfiguration.S3StorageSection(config);
        Assert.That(s3.AccessKey, Is.EqualTo("ak"));
        Assert.That(s3.SecretAccessKey, Is.EqualTo("sk"));
        Assert.That(s3.ServiceUrl, Is.EqualTo("https://s3.example.com"));
        Assert.That(s3.Region, Is.EqualTo("eu-1"));
        Assert.That(s3.ForcePathStyle, Is.True);

        var payload = new OdinConfiguration.S3PayloadStorageSection(config);
        Assert.That(payload.Enabled, Is.True);
        Assert.That(payload.BucketName, Is.EqualTo("payload-bucket"));
        Assert.That(payload.RootPath, Is.EqualTo("payloads")); // default

        var inbox = new OdinConfiguration.S3InboxStorageSection(config);
        Assert.That(inbox.Enabled, Is.True);
        Assert.That(inbox.BucketName, Is.EqualTo("inbox-bucket"));
        Assert.That(inbox.RootPath, Is.EqualTo("inbox")); // default
    }

    [Test]
    public void Disabled_Sections_DoNotRequireBucket()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var payload = new OdinConfiguration.S3PayloadStorageSection(config);
        Assert.That(payload.Enabled, Is.False);

        var inbox = new OdinConfiguration.S3InboxStorageSection(config);
        Assert.That(inbox.Enabled, Is.False);
    }
}
