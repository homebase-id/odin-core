using System;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Registry;
using Odin.Services.Registry.LastSeen;

// Grok v4 was here
namespace Odin.Services.Tests.Registry.LastSeen;

[TestFixture]
public class LastSeenServiceTests
{
    private LastSeenService _service = new ();

    [SetUp]
    public void Setup()
    {
        _service = new LastSeenService();
    }

    [Test]
    public void LastSeenNow_WithIdentityRegistration_SetsLastSeenToNow()
    {
        var now = UnixTimeUtc.Now();
        var registration = new IdentityRegistration { Id = Guid.NewGuid(), PrimaryDomainName = "test.domain" };

        _service.LastSeenNow(registration);

        var lastSeenId = _service.GetLastSeen(registration.Id);
        var lastSeenDomain = _service.GetLastSeen(registration.PrimaryDomainName);

        Assert.That(lastSeenId, Is.Not.Null);
        Assert.That(lastSeenDomain, Is.Not.Null);
        // Approximate check due to timing; in practice, use a mockable time provider for exactness
        Assert.That(lastSeenId!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
        Assert.That(lastSeenDomain!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
    }

    [Test]
    public void LastSeenNow_WithIdAndDomain_SetsLastSeenToNow()
    {
        var now = UnixTimeUtc.Now();
        var id = Guid.NewGuid();
        var domain = "test.domain";

        _service.LastSeenNow(id, domain);

        var lastSeenId = _service.GetLastSeen(id);
        var lastSeenDomain = _service.GetLastSeen(domain);

        Assert.That(lastSeenId, Is.Not.Null);
        Assert.That(lastSeenDomain, Is.Not.Null);
        Assert.That(lastSeenId!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
        Assert.That(lastSeenDomain!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
    }

    [Test]
    public void PutLastSeen_WithIdentityRegistration_SetsSpecificLastSeen()
    {
        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var registration = new IdentityRegistration { Id = Guid.NewGuid(), PrimaryDomainName = "test.domain" };

        _service.PutLastSeen(registration, lastSeen);

        Assert.That(_service.GetLastSeen(registration.Id), Is.EqualTo(lastSeen));
        Assert.That(_service.GetLastSeen(registration.PrimaryDomainName), Is.EqualTo(lastSeen));
    }

    [Test]
    public void PutLastSeen_WithIdAndDomain_SetsSpecificLastSeen()
    {
        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var id = Guid.NewGuid();
        var domain = "test.domain";

        _service.PutLastSeen(id, domain, lastSeen);

        Assert.That(_service.GetLastSeen(id), Is.EqualTo(lastSeen));
        Assert.That(_service.GetLastSeen(domain), Is.EqualTo(lastSeen));
    }

    [Test]
    public void PutLastSeen_WithRegistrationsRecord_SetsLastSeenIfNotNull()
    {
        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var record = new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            primaryDomainName = "test.domain",
            lastSeen = lastSeen
        };

        _service.PutLastSeen(record);

        Assert.That(_service.GetLastSeen(record.identityId), Is.EqualTo(lastSeen));
        Assert.That(_service.GetLastSeen(record.primaryDomainName), Is.EqualTo(lastSeen));
    }

    [Test]
    public void PutLastSeen_WithNullRegistrationsRecord_DoesNothing()
    {
        _service.PutLastSeen(null);

        Assert.That(_service.AllByIdentityId, Is.Empty);
        Assert.That(_service.AllByDomain, Is.Empty);
    }

    [Test]
    public void PutLastSeen_WithRegistrationsRecordNullLastSeen_DoesNothing()
    {
        var record = new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            primaryDomainName = "test.domain",
            lastSeen = null
        };

        _service.PutLastSeen(record);

        Assert.That(_service.GetLastSeen(record.identityId), Is.Null);
        Assert.That(_service.GetLastSeen(record.primaryDomainName), Is.Null);
    }

    [Test]
    public void GetLastSeen_WithUnknownId_ReturnsNull()
    {
        var result = _service.GetLastSeen(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLastSeen_WithUnknownDomain_ReturnsNull()
    {
        var result = _service.GetLastSeen("unknown.domain");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void AllByIdentityId_ReturnsCopyOfDictionary()
    {
        var id = Guid.NewGuid();
        var lastSeen = UnixTimeUtc.Now();
        _service.PutLastSeen(id, "test.domain", lastSeen);

        var all = _service.AllByIdentityId;

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[id], Is.EqualTo(lastSeen));
        // Verify it's a copy (modifying it shouldn't affect internal state)
        all.Clear();
        Assert.That(_service.AllByIdentityId, Has.Count.EqualTo(1));
    }

    [Test]
    public void AllByDomain_ReturnsCopyOfDictionary()
    {
        var domain = "test.domain";
        var lastSeen = UnixTimeUtc.Now();
        _service.PutLastSeen(Guid.NewGuid(), domain, lastSeen);

        var all = _service.AllByDomain;

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[domain], Is.EqualTo(lastSeen));
        // Verify it's a copy
        all.Clear();
        Assert.That(_service.AllByDomain, Has.Count.EqualTo(1));
    }

    [Test]
    public void UpdatesOverwriteExistingValues()
    {
        var id = Guid.NewGuid();
        var domain = "test.domain";
        var initial = UnixTimeUtc.Now().AddSeconds(-200);
        var updated = UnixTimeUtc.Now().AddSeconds(-100);

        _service.PutLastSeen(id, domain, initial);
        _service.PutLastSeen(id, domain, updated);

        Assert.That(_service.GetLastSeen(id), Is.EqualTo(updated));
        Assert.That(_service.GetLastSeen(domain), Is.EqualTo(updated));
    }
}