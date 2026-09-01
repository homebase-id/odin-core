using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests.IdentityJsonExport;

public class IdentityImportPreconditionTests
{
    private const string IdentityDomain = "frodo.dotyou.cloud";

    private Guid _identityId;
    private string _tempFolder = "";
    private TestServices _services = null!;
    private ILifetimeScope _scope = null!;

    [SetUp]
    public void Setup()
    {
        _identityId = Guid.NewGuid();
        _tempFolder = TempDirectory.Create();
        _services = new TestServices();
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        _services = null!;
        _scope = null!;
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task<(SystemDatabase sys, IdentityDatabase id)> InitAsync()
    {
        _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
        return (_scope.Resolve<SystemDatabase>(), _scope.Resolve<IdentityDatabase>());
    }

    private async Task<ExportHeader> MatchingHeaderAsync(SystemDatabase sys, IdentityDatabase id)
    {
        return new ExportHeader
        {
            FormatVersion = IdentityExportFile.CurrentFormatVersion,
            IdentityId = _identityId,
            Domain = IdentityDomain,
            TableVersions = new Dictionary<string, Dictionary<string, long>>
            {
                [IdentityExportFile.DbSystem] = await sys.GetTableVersionsAsync(),
                [IdentityExportFile.DbIdentity] = await id.GetTableVersionsAsync(),
            },
        };
    }

    [Test]
    public async Task CheckAsync_PassesOnAnEmptyMatchingTarget()
    {
        var (sys, id) = await InitAsync();
        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations, Is.Empty);
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheIdentityIdAlreadyExists()
    {
        var (sys, id) = await InitAsync();
        await sys.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = _identityId,
            email = "a@b.c",
            primaryDomainName = "someone-else.dotyou.cloud",
            firstRunToken = Guid.NewGuid().ToString(),
            disabled = false,
            planId = "free",
            enablePublicWebPresence = false,
            json = "{}",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("identityId")), Is.True,
            "Expected an identityId collision. Got: " + string.Join(" | ", violations));
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheDomainAlreadyExists_IgnoringCase()
    {
        var (sys, id) = await InitAsync();
        await sys.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            email = "a@b.c",
            primaryDomainName = "FRODO.DOTYOU.CLOUD",
            firstRunToken = Guid.NewGuid().ToString(),
            disabled = false,
            planId = "free",
            enablePublicWebPresence = false,
            json = "{}",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("domain")), Is.True,
            "Expected a domain collision. Got: " + string.Join(" | ", violations));
    }

    [Test]
    public async Task CheckAsync_FailsOnALeftoverCertificateRowWithNoRegistration()
    {
        var (sys, id) = await InitAsync();
        await sys.Certificates.InsertAsync(new CertificatesRecord
        {
            domain = new OdinId(IdentityDomain),
            privateKey = "pk",
            certificate = "cert",
            expiration = UnixTimeUtc.Now(),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = "c",
            lastError = "",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("Certificates")), Is.True,
            "Expected a Certificates collision. Got: " + string.Join(" | ", violations));
    }

    // DkimKeys is keyed by (domain, selector), so like Certificates it outlives the
    // registration and neither of the two checks above would catch it.
    [Test]
    public async Task CheckAsync_FailsOnLeftoverDkimKeyRowsWithNoRegistration()
    {
        var (sys, id) = await InitAsync();
        await sys.DkimKeys.InsertAsync(new DkimKeysRecord
        {
            domain = new OdinId(IdentityDomain),
            selector = "s1",
            algorithm = "ed25519",
            publicKey = "pub",
            privateKey = "priv",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("DkimKeys")), Is.True,
            "Expected a DkimKeys collision. Got: " + string.Join(" | ", violations));
    }

    // Requirement 10: one differing table blocks everything, even though the rest match.
    [Test]
    public async Task CheckAsync_FailsWhenASingleTableVersionDiffers()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["Circle"] = 111111111111L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Circle")), Is.True,
            "Expected a Circle version mismatch. Got: " + string.Join(" | ", violations));
    }

    // Requirement 10: skipping a table on import does not exempt it from the check.
    [Test]
    public async Task CheckAsync_FailsWhenASkippedTablesVersionDiffers()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["Outbox"] = 111111111111L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Outbox")), Is.True,
            "Expected an Outbox version mismatch even though Outbox is skipped on import.");
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheHeaderIsMissingATableTheTargetHas()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity].Remove("Circle");

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Circle")), Is.True,
            "A table on the target but absent from the header is a mismatch.");
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheHeaderHasATableTheTargetDoesNot()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["TableFromTheFuture"] = 202700000000L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("TableFromTheFuture")), Is.True,
            "A table in the header but absent from the target is a mismatch.");
    }

    [Test]
    public async Task CheckAsync_ReportsEveryDifferenceNotJustTheFirst()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["Circle"] = 111111111111L;
        header.TableVersions[IdentityExportFile.DbIdentity]["Drives"] = 222222222222L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Circle")), Is.True);
        Assert.That(violations.Any(v => v.Contains("Drives")), Is.True);
    }

    [Test]
    public async Task CheckAsync_FailsWhenFormatVersionIsNewerThanThisBinary()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.FormatVersion = IdentityExportFile.CurrentFormatVersion + 1;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("formatVersion")), Is.True);
    }
}
