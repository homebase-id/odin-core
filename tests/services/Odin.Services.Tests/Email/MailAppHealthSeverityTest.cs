#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Services.Dns.Health;
using Odin.Services.Email;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Email;

/// <summary>
/// The app's Email setup section renders this verdict as green / orange / red and derives
/// nothing itself (#1862), so the mapping is pinned here, as data.
/// </summary>
public class MailAppHealthSeverityTest
{
    [TestCase(Case.BrokenRecord, MailHealthSeverity.Error)]
    [TestCase(Case.FailedCheck, MailHealthSeverity.Error)]
    [TestCase(Case.DsMismatch, MailHealthSeverity.Error)]
    [TestCase(Case.DsMissing, MailHealthSeverity.Warning)]
    [TestCase(Case.ParentUnsigned, MailHealthSeverity.Warning)]
    [TestCase(Case.ZoneUnsigned, MailHealthSeverity.Warning)]
    [TestCase(Case.CouldNotCheck, MailHealthSeverity.Warning)]
    [TestCase(Case.Secure, MailHealthSeverity.Ok)]
    [TestCase(Case.Inherited, MailHealthSeverity.Ok)]
    [TestCase(Case.DnskeyLookupFailed, MailHealthSeverity.Ok)]
    [TestCase(Case.TenantMailOff, MailHealthSeverity.Ok)]
    public void ItShouldGradeEmailHealth(Case c, MailHealthSeverity expected)
    {
        var health = Build(c);
        Assert.That(health.Severity, Is.EqualTo(expected), $"{c} -> {health.Severity}");
    }

    public enum Case
    {
        BrokenRecord, FailedCheck, DsMismatch, DsMissing, ParentUnsigned, ZoneUnsigned, CouldNotCheck,
        Secure, Inherited, DnskeyLookupFailed, TenantMailOff,
    }

    private static MailAppHealthResult Build(Case c)
    {
        static DnssecHealthResult Dnssec(DnsHealthDnssecStatus status, bool lookupFailed = false) =>
            new() { Status = status, LookupFailed = lookupFailed };

        var secure = Dnssec(DnsHealthDnssecStatus.Secure);
        return c switch
        {
            Case.BrokenRecord => new MailAppHealthResult
            {
                TenantMailEnabled = true, Dnssec = secure,
                BrokenRecords = [new DnsConfig { Type = "TXT", Name = "_dmarc", Status = DnsLookupRecordStatus.DomainOrRecordNotFound }],
            },
            Case.FailedCheck => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = secure, Errors = ["DKIM pair proof failed"] },
            Case.DsMismatch => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = Dnssec(DnsHealthDnssecStatus.DsMismatch) },
            Case.DsMissing => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = Dnssec(DnsHealthDnssecStatus.DsMissing) },
            Case.ParentUnsigned => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = Dnssec(DnsHealthDnssecStatus.ParentUnsigned) },
            Case.ZoneUnsigned => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = Dnssec(DnsHealthDnssecStatus.ZoneUnsigned) },
            Case.CouldNotCheck => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = secure, Warnings = ["Outbound sending could not be checked right now"] },
            Case.Secure => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = secure },
            Case.Inherited => new MailAppHealthResult { TenantMailEnabled = true, Dnssec = Dnssec(DnsHealthDnssecStatus.Inherited) },
            Case.DnskeyLookupFailed => new MailAppHealthResult
            {
                TenantMailEnabled = true, Dnssec = Dnssec(DnsHealthDnssecStatus.ZoneUnsigned, lookupFailed: true),
            },
            _ => new MailAppHealthResult { TenantMailEnabled = false },
        };
    }
}
