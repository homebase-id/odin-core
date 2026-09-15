using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Everything that must hold before a single row is written.
//
// Returns every violation rather than throwing on the first, so one run tells the
// operator the full extent of the problem instead of making them re-run to discover
// the next one.
public static class IdentityImportPreconditions
{
    public static async Task<List<string>> CheckAsync(
        ExportHeader header,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase)
    {
        var violations = new List<string>();

        if (header.FormatVersion > IdentityExportFile.CurrentFormatVersion)
        {
            violations.Add(
                $"File formatVersion {header.FormatVersion} is newer than this binary understands "
                + $"({IdentityExportFile.CurrentFormatVersion}).");
        }

        // 1. Registrations: identityId or domain, either is a hard stop.
        var registrations = await targetSystemDatabase.Registrations.GetAllAsync();
        if (registrations.Any(r => r.identityId == header.IdentityId))
        {
            violations.Add($"Target already has a registration with identityId {header.IdentityId}.");
        }
        if (registrations.Any(r => r.primaryDomainName.Equals(header.Domain, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add($"Target already has a registration for domain {header.Domain}.");
        }

        // 2. Certificates is keyed by domain, so it survives independently of the
        //    registration. DataImporter.DeleteIdentityFromSystemDataAsync has to delete
        //    both rows for exactly this reason.
        var certificate = await targetSystemDatabase.Certificates.GetAsync(new OdinId(header.Domain));
        if (certificate != null)
        {
            violations.Add($"Target already has a Certificates row for domain {header.Domain}.");
        }

        // 3. DkimKeys is keyed by (domain, selector) and outlives the registration for
        //    the same reason Certificates does; DataImporter.DeleteIdentityFromSystemDataAsync
        //    deletes it by domain too. One identity owns several rows here, so count them
        //    rather than testing for a single row.
        var dkimKeys = await targetSystemDatabase.DkimKeys.GetByDomainAsync(new OdinId(header.Domain));
        if (dkimKeys.Count > 0)
        {
            violations.Add($"Target already has {dkimKeys.Count} DkimKeys row(s) for domain {header.Domain}.");
        }

        // 4. On Postgres every identity shares one set of physical tables, and
        //    DeleteRegistration never purges them, so identity rows can outlive the
        //    registration and checks 1 and 2 would both pass.
        var orphanRows = await targetIdentityDatabase.CountRowsForIdentityAsync(header.IdentityId);
        if (orphanRows > 0)
        {
            violations.Add(
                $"Target identity tables already hold {orphanRows} row(s) for identityId {header.IdentityId}.");
        }

        // 5. All-or-nothing table version match, in both directions.
        violations.AddRange(CompareTableVersions(
            IdentityExportFile.DbSystem,
            header.TableVersions.GetValueOrDefault(IdentityExportFile.DbSystem) ?? new Dictionary<string, long>(),
            await targetSystemDatabase.GetTableVersionsAsync()));

        violations.AddRange(CompareTableVersions(
            IdentityExportFile.DbIdentity,
            header.TableVersions.GetValueOrDefault(IdentityExportFile.DbIdentity) ?? new Dictionary<string, long>(),
            await targetIdentityDatabase.GetTableVersionsAsync()));

        return violations;
    }

    // Table sets must be identical, not merely overlapping. A table present on one
    // side and absent on the other is as much a mismatch as a differing version.
    private static IEnumerable<string> CompareTableVersions(
        string db,
        Dictionary<string, long> fromFile,
        Dictionary<string, long> onTarget)
    {
        foreach (var (table, fileVersion) in fromFile.OrderBy(kv => kv.Key))
        {
            if (!onTarget.TryGetValue(table, out var targetVersion))
            {
                yield return $"{db}.{table}: present in the file, absent on the target.";
            }
            else if (fileVersion != targetVersion)
            {
                yield return $"{db}.{table}: file version {fileVersion}, target version {targetVersion}.";
            }
        }

        foreach (var table in onTarget.Keys.Where(t => !fromFile.ContainsKey(t)).OrderBy(t => t))
        {
            yield return $"{db}.{table}: present on the target, absent from the file.";
        }
    }
}
