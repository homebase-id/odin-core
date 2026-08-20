using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Services.Email;
using Odin.Test.Helpers;

namespace Odin.Services.Tests.Email;

[TestFixture]
public class EmailPublicKeyServiceTests
{
    private string _tempDir = "";
    private TestServices? _testServices;

    [SetUp]
    public void Setup()
    {
        _tempDir = TempDirectory.Create();
        _testServices = new TestServices();
    }

    [TearDown]
    public void TearDown()
    {
        _testServices?.Dispose();
        Directory.Delete(_tempDir, true);
    }

    private async Task<EmailPublicKeyService> CreateServiceAsync(DatabaseType databaseType)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, Guid.NewGuid());
        return new EmailPublicKeyService(services.Resolve<IdentityDatabase>());
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldReturnNullWhenNothingIsPublished(DatabaseType databaseType)
    {
        var service = await CreateServiceAsync(databaseType);
        Assert.That(await service.GetPublishedKeyAsync(), Is.Null);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldPublishAndRoundTripTheCertificate(DatabaseType databaseType)
    {
        var service = await CreateServiceAsync(databaseType);
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial("frodo@frodo.dotyou.cloud");

        await service.PublishAsync(material.PublicCertificateArmored);

        var published = await service.GetPublishedKeyAsync();
        Assert.That(published, Is.Not.Null);
        Assert.That(published!.PublicCertificateArmored, Is.EqualTo(material.PublicCertificateArmored));
        Assert.That(published.FingerprintHex, Is.EqualTo(material.FingerprintHex));
        Assert.That(published.PublishedAt.milliseconds, Is.GreaterThan(0));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldReplaceOnRotationAndDeleteOnUnpublish(DatabaseType databaseType)
    {
        var service = await CreateServiceAsync(databaseType);

        var first = OpenPgpKeyManagement.GenerateP384KeyMaterial("frodo@frodo.dotyou.cloud");
        var second = OpenPgpKeyManagement.GenerateP384KeyMaterial("frodo@frodo.dotyou.cloud");

        await service.PublishAsync(first.PublicCertificateArmored);
        await service.PublishAsync(second.PublicCertificateArmored);

        var published = await service.GetPublishedKeyAsync();
        Assert.That(published!.FingerprintHex, Is.EqualTo(second.FingerprintHex));

        await service.UnpublishAsync();
        Assert.That(await service.GetPublishedKeyAsync(), Is.Null);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldRefuseGarbageAndSecretKeys(DatabaseType databaseType)
    {
        var service = await CreateServiceAsync(databaseType);

        Assert.ThrowsAsync<OdinSystemException>(() => service.PublishAsync("not a certificate"));

        // A secret keyring must never be publishable
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial("frodo@frodo.dotyou.cloud");
        Assert.ThrowsAsync<OdinSystemException>(() => service.PublishAsync(material.SecretKeyArmored));

        Assert.That(await service.GetPublishedKeyAsync(), Is.Null, "nothing may be stored after refusals");
    }
}
