using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Admin.Tenants;

namespace Odin.Services.Tests.Admin.Tenants;

// #1863: what `odin-admin tenants list --inactive-days N` keeps
public class TenantModelTests
{
    private static readonly UnixTimeUtc Now = UnixTimeUtc.FromDateTime(new System.DateTime(2026, 10, 6, 12, 0, 0, System.DateTimeKind.Utc));

    [Test]
    public void NeverSeenCountsAsInactive()
    {
        Assert.That(new TenantModel { LastActivity = null }.InactiveFor(90, Now), Is.True);
        Assert.That(new TenantModel { LastActivity = null }.InactiveFor(0, Now), Is.True);
    }

    [Test]
    public void InactiveOnlyWhenTheLastActivityIsOlderThanTheDays()
    {
        Assert.That(new TenantModel { LastActivity = Now.AddDays(-91) }.InactiveFor(90, Now), Is.True);
        Assert.That(new TenantModel { LastActivity = Now.AddDays(-89) }.InactiveFor(90, Now), Is.False);
        Assert.That(new TenantModel { LastActivity = Now.AddDays(-90) }.InactiveFor(90, Now), Is.False, "exactly N days ago is not older");
        Assert.That(new TenantModel { LastActivity = Now.AddSeconds(-1) }.InactiveFor(0, Now), Is.True, "0 days: anything before now");
    }
}
