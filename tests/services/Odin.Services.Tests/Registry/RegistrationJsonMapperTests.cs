using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

public class RegistrationJsonMapperTests
{
    private static IdentityRegistration Registration(TenantStatus status, DisabledReason? reason, UnixTimeUtc? changedAt)
    {
        return new IdentityRegistration
        {
            PrimaryDomainName = "frodo.dotyou.cloud",
            Status = status,
            DisabledReason = reason,
            StatusChangedAt = changedAt
        };
    }

    [TestCase(TenantStatus.Active, null)]
    [TestCase(TenantStatus.OutOfQuota, null)]
    [TestCase(TenantStatus.Paused, null)]
    [TestCase(TenantStatus.Disabled, DisabledReason.Admin)]
    [TestCase(TenantStatus.Disabled, DisabledReason.PendingDeletion)]
    [TestCase(TenantStatus.Disabled, DisabledReason.Moved)]
    public void RoundTripsEveryState(TenantStatus status, DisabledReason? reason)
    {
        var changedAt = new UnixTimeUtc(1_757_000_000_000);
        var source = Registration(status, reason, changedAt);
        var json = RegistrationJsonMapper.ToJson(source);

        var target = new IdentityRegistration();
        var valid = RegistrationJsonMapper.Apply(target, disabledColumn: source.Status == TenantStatus.Disabled, json);

        Assert.That(valid, Is.True);
        Assert.That(target.Status, Is.EqualTo(status));
        Assert.That(target.DisabledReason, Is.EqualTo(reason));
        Assert.That(target.StatusChangedAt, Is.EqualTo(changedAt));
    }

    [Test]
    public void WritesReadableNames()
    {
        var json = RegistrationJsonMapper.ToJson(Registration(TenantStatus.Disabled, DisabledReason.PendingDeletion, null));
        Assert.That(json, Does.Contain("\"disabled\""));
        Assert.That(json, Does.Contain("\"pendingDeletion\""));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void LegacyRowWithoutJsonFollowsTheDisabledColumn(string json)
    {
        var disabled = new IdentityRegistration();
        Assert.That(RegistrationJsonMapper.Apply(disabled, disabledColumn: true, json), Is.True);
        Assert.That(disabled.Status, Is.EqualTo(TenantStatus.Disabled));
        Assert.That(disabled.DisabledReason, Is.EqualTo(DisabledReason.Admin));
        Assert.That(disabled.StatusChangedAt, Is.Null);

        var enabled = new IdentityRegistration();
        Assert.That(RegistrationJsonMapper.Apply(enabled, disabledColumn: false, json), Is.True);
        Assert.That(enabled.Status, Is.EqualTo(TenantStatus.Active));
        Assert.That(enabled.DisabledReason, Is.Null);
    }

    [TestCase(TenantStatus.Active)]
    [TestCase(TenantStatus.OutOfQuota)]
    [TestCase(TenantStatus.Paused)]
    public void OlderNodeDisablingWinsOverJson(TenantStatus jsonStatus)
    {
        // An older node set disabled=true without knowing about the json status
        var json = RegistrationJsonMapper.ToJson(Registration(jsonStatus, null, new UnixTimeUtc(1)));

        var target = new IdentityRegistration();
        RegistrationJsonMapper.Apply(target, disabledColumn: true, json);

        Assert.That(target.Status, Is.EqualTo(TenantStatus.Disabled));
        Assert.That(target.DisabledReason, Is.EqualTo(DisabledReason.Admin));
        Assert.That(target.StatusChangedAt, Is.Null);
    }

    [Test]
    public void OlderNodeEnablingWinsOverJson()
    {
        // An older node set disabled=false on an identity this version had disabled
        var json = RegistrationJsonMapper.ToJson(Registration(TenantStatus.Disabled, DisabledReason.Moved, new UnixTimeUtc(1)));

        var target = Registration(TenantStatus.Disabled, DisabledReason.Moved, new UnixTimeUtc(1));
        RegistrationJsonMapper.Apply(target, disabledColumn: false, json);

        Assert.That(target.Status, Is.EqualTo(TenantStatus.Active));
        Assert.That(target.DisabledReason, Is.Null);
        Assert.That(target.StatusChangedAt, Is.Null);
    }

    [Test]
    public void DisabledJsonWithoutReasonReadsAsAdmin()
    {
        var target = new IdentityRegistration();
        RegistrationJsonMapper.Apply(target, disabledColumn: true, "{\"status\":\"disabled\"}");
        Assert.That(target.DisabledReason, Is.EqualTo(DisabledReason.Admin));
    }

    [Test]
    public void ReasonOnANonDisabledStatusIsDropped()
    {
        var target = new IdentityRegistration();
        RegistrationJsonMapper.Apply(target, disabledColumn: false, "{\"status\":\"paused\",\"disabledReason\":\"moved\"}");
        Assert.That(target.Status, Is.EqualTo(TenantStatus.Paused));
        Assert.That(target.DisabledReason, Is.Null);
    }

    [TestCase("not json")]
    [TestCase("{\"status\":\"frozen\"}")]
    [TestCase("{\"status\":42}")]
    [TestCase("{\"status\":\"disabled\",\"disabledReason\":\"stolen\"}")]
    [TestCase("[1,2,3]")]
    public void UnreadableJsonFallsBackToTheColumnAndReportsIt(string json)
    {
        var disabled = new IdentityRegistration();
        Assert.That(RegistrationJsonMapper.Apply(disabled, disabledColumn: true, json), Is.False);
        Assert.That(disabled.Status, Is.EqualTo(TenantStatus.Disabled));
        Assert.That(disabled.DisabledReason, Is.EqualTo(DisabledReason.Admin));

        var enabled = new IdentityRegistration();
        Assert.That(RegistrationJsonMapper.Apply(enabled, disabledColumn: false, json), Is.False);
        Assert.That(enabled.Status, Is.EqualTo(TenantStatus.Active));
    }

    [Test]
    public void UnknownJsonFieldsAreIgnored()
    {
        var target = new IdentityRegistration();
        var valid = RegistrationJsonMapper.Apply(target, disabledColumn: false, "{\"status\":\"paused\",\"somethingNew\":1}");
        Assert.That(valid, Is.True);
        Assert.That(target.Status, Is.EqualTo(TenantStatus.Paused));
    }
}
