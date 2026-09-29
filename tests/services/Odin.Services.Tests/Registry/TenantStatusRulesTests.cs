using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

public class TenantStatusRulesTests
{
    private static readonly TenantStatus[] AllStatuses = Enum.GetValues<TenantStatus>();

    // Every (status, reason) combination an identity can be in
    private static IEnumerable<(TenantStatus Status, DisabledReason? Reason)> AllStates()
    {
        foreach (var status in AllStatuses)
        {
            if (status == TenantStatus.Disabled)
            {
                foreach (var reason in Enum.GetValues<DisabledReason>())
                {
                    yield return (status, reason);
                }
            }
            else
            {
                yield return (status, null);
            }
        }
    }

    private static IEnumerable<TestCaseData> AllTransitions()
    {
        foreach (var from in AllStates())
        {
            foreach (var to in AllStates())
            {
                var isMoved = from is { Status: TenantStatus.Disabled, Reason: DisabledReason.Moved };
                var leavesMoved = isMoved &&
                                  to is not { Status: TenantStatus.Disabled, Reason: DisabledReason.Moved or DisabledReason.PendingDeletion };
                var skipsEnable = from.Status == TenantStatus.Disabled && to.Status is TenantStatus.OutOfQuota or TenantStatus.Paused;
                var allowed = !leavesMoved && !skipsEnable;
                yield return new TestCaseData(from.Status, from.Reason, to.Status, to.Reason, allowed)
                    .SetName($"{from.Status}/{from.Reason?.ToString() ?? "-"} -> {to.Status}/{to.Reason?.ToString() ?? "-"} allowed={allowed}");
            }
        }
    }

    [TestCaseSource(nameof(AllTransitions))]
    public void ValidateAllowsEverythingExceptLeavingMovedOrLeavingDisabledOtherThanByEnabling(
        TenantStatus fromStatus, DisabledReason? fromReason, TenantStatus toStatus, DisabledReason? toReason, bool allowed)
    {
        if (allowed)
        {
            Assert.DoesNotThrow(() => TenantStatusRules.Validate(fromStatus, fromReason, toStatus, toReason));
        }
        else
        {
            Assert.Throws<OdinClientException>(() => TenantStatusRules.Validate(fromStatus, fromReason, toStatus, toReason));
        }
    }

    [TestCase(TenantStatus.Active)]
    [TestCase(TenantStatus.OutOfQuota)]
    [TestCase(TenantStatus.Paused)]
    public void ValidateRefusesAReasonForAnythingButDisabled(TenantStatus status)
    {
        var e = Assert.Throws<OdinClientException>(() =>
            TenantStatusRules.Validate(TenantStatus.Active, null, status, DisabledReason.Admin));
        Assert.That(e!.Message, Does.Contain("only valid for the disabled status"));
    }

    [Test]
    public void ValidateRefusesDisabledWithoutAReason()
    {
        Assert.Throws<OdinClientException>(() =>
            TenantStatusRules.Validate(TenantStatus.Active, null, TenantStatus.Disabled, null));
    }

    [Test]
    public void ValidateRefusesUnknownValues()
    {
        Assert.Throws<OdinClientException>(() =>
            TenantStatusRules.Validate(TenantStatus.Active, null, (TenantStatus)99, null));
        Assert.Throws<OdinClientException>(() =>
            TenantStatusRules.Validate(TenantStatus.Active, null, TenantStatus.Disabled, (DisabledReason)99));
    }

    [Test]
    public void NormalizeReasonDefaultsDisabledToAdminAndLeavesOthersAlone()
    {
        Assert.That(TenantStatusRules.NormalizeReason(TenantStatus.Disabled, null), Is.EqualTo(DisabledReason.Admin));
        Assert.That(TenantStatusRules.NormalizeReason(TenantStatus.Disabled, DisabledReason.Moved), Is.EqualTo(DisabledReason.Moved));
        Assert.That(TenantStatusRules.NormalizeReason(TenantStatus.Paused, null), Is.Null);

        // Not silently dropped: Validate must still see it and refuse
        Assert.That(TenantStatusRules.NormalizeReason(TenantStatus.Paused, DisabledReason.Admin), Is.EqualTo(DisabledReason.Admin));
    }

    [TestCase(TenantStatus.Active, true)]
    [TestCase(TenantStatus.OutOfQuota, true)]
    [TestCase(TenantStatus.Paused, false)]
    [TestCase(TenantStatus.Disabled, false)]
    public void RunsBackgroundServicesOnlyWhenServing(TenantStatus status, bool expected)
    {
        Assert.That(TenantStatusRules.RunsBackgroundServices(status), Is.EqualTo(expected));
    }

    [Test]
    public void MovedAndDisabledRefusalsExplainThemselves()
    {
        var moved = Assert.Throws<OdinClientException>(() =>
            TenantStatusRules.Validate(TenantStatus.Disabled, DisabledReason.Moved, TenantStatus.Active, null));
        Assert.That(moved!.Message, Does.Contain("moved"));

        var disabled = Assert.Throws<OdinClientException>(() =>
            TenantStatusRules.Validate(TenantStatus.Disabled, DisabledReason.Admin, TenantStatus.Paused, null));
        Assert.That(disabled!.Message, Does.Contain("can only be enabled"));
    }

    [Test]
    public void RunsBackgroundServicesRefusesAStatusWithoutADecision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TenantStatusRules.RunsBackgroundServices((TenantStatus)99));
    }

    [TestCase("active", TenantStatus.Active)]
    [TestCase("Paused", TenantStatus.Paused)]
    [TestCase("out-of-quota", TenantStatus.OutOfQuota)]
    [TestCase("OUT_OF_QUOTA", TenantStatus.OutOfQuota)]
    [TestCase(" disabled ", TenantStatus.Disabled)]
    public void TryParseAcceptsNames(string value, TenantStatus expected)
    {
        Assert.That(TenantStatusRules.TryParse<TenantStatus>(value, out var status), Is.True);
        Assert.That(status, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("2")]
    [TestCase("frozen")]
    [TestCase("active,paused")]
    public void TryParseRefusesEverythingElse(string value)
    {
        Assert.That(TenantStatusRules.TryParse<TenantStatus>(value, out _), Is.False);
    }

    [TestCase("pending-deletion", DisabledReason.PendingDeletion)]
    [TestCase("moved", DisabledReason.Moved)]
    public void TryParseAcceptsReasons(string value, DisabledReason expected)
    {
        Assert.That(TenantStatusRules.TryParse<DisabledReason>(value, out var reason), Is.True);
        Assert.That(reason, Is.EqualTo(expected));
    }
    [Test]
    public void ExportSettleTimeCoversTwoCatchUpsAndAMinute()
    {
        Assert.That(TenantStatusRules.ExportSettleTime(30), Is.EqualTo(TimeSpan.FromSeconds(120)));
        Assert.That(TenantStatusRules.ExportSettleTime(1), Is.EqualTo(TimeSpan.FromSeconds(62)));
    }

    private static readonly UnixTimeUtc Now = new(1_760_000_000_000);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(120);

    [TestCase(TenantStatus.Active)]
    [TestCase(TenantStatus.OutOfQuota)]
    public void ExportMustWaitForARunningIdentityToBePaused(TenantStatus status)
    {
        var reason = TenantStatusRules.WhyExportMustWait(status, Now.AddSeconds(-3600), Now, Settle);
        Assert.That(reason, Does.Contain("pause it first"), reason);
    }

    [TestCase(TenantStatus.Paused)]
    [TestCase(TenantStatus.Disabled)]
    public void ExportMustWaitUntilTheIdentityHasSettled(TenantStatus status)
    {
        var reason = TenantStatusRules.WhyExportMustWait(status, Now.AddSeconds(-40), Now, Settle);
        Assert.That(reason, Does.Contain("40 s ago").And.Contain("allowed in 80 s"), reason);
    }

    [TestCase(TenantStatus.Paused)]
    [TestCase(TenantStatus.Disabled)]
    public void ExportMayStartOnceTheIdentityHasSettled(TenantStatus status)
    {
        var reason = TenantStatusRules.WhyExportMustWait(status, Now.AddSeconds(-120), Now, Settle);
        Assert.That(reason, Is.Null, reason);
    }

    [Test]
    public void ExportMustWaitWhenItCannotTellHowLongTheIdentityHasBeenStill()
    {
        var reason = TenantStatusRules.WhyExportMustWait(TenantStatus.Paused, null, Now, Settle);
        Assert.That(reason, Does.Contain("pause it again"), reason);
    }

    [Test]
    public void ExportWaitsTheFullSettleTimeWhenTheChangeIsStampedInTheFuture()
    {
        // Another node's clock ahead of this one
        var reason = TenantStatusRules.WhyExportMustWait(TenantStatus.Paused, Now.AddSeconds(30), Now, Settle);
        Assert.That(reason, Does.Contain("0 s ago").And.Contain("allowed in 120 s"), reason);
    }
}
