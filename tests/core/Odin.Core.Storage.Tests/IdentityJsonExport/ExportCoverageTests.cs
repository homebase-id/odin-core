using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Tests.IdentityJsonExport;

// DataImporter needs a hand-maintained list of tables, policed by a test that greps
// its source as text. The generated aggregate cannot drift the same way, and this
// test is the runtime proof: ExportableTables is generated from the same table
// definitions as TableTypes, so any table added to the generator appears in both.
public class ExportCoverageTests
{
    [Test]
    public void ExportableTables_CoversEveryIdentityTable()
    {
        var fromTableTypes = IdentityDatabase.TableTypes
            .Select(t => t.Name.StartsWith("Table") ? t.Name.Substring("Table".Length) : t.Name)
            .OrderBy(n => n)
            .ToList();

        var fromExport = IdentityDatabase.ExportableTables.OrderBy(n => n).ToList();

        Assert.That(fromExport, Is.EqualTo(fromTableTypes),
            "IdentityDatabase.ExportableTables does not match TableTypes. Re-run the generator.");
    }

    [Test]
    public void ExportableRecordTypes_HasAnEntryForEveryExportableTable()
    {
        var missing = IdentityDatabase.ExportableTables
            .Where(name => !IdentityDatabase.ExportableRecordTypes.ContainsKey(name))
            .ToList();

        Assert.That(missing, Is.Empty,
            "ExportableRecordTypes is missing: " + string.Join(", ", missing));
    }

    [Test]
    public void ExportableRecordTypes_MapsToRealRecordTypes()
    {
        foreach (var (name, type) in IdentityDatabase.ExportableRecordTypes)
        {
            Assert.That(type.Name, Is.EqualTo(name + "Record"),
                $"Table {name} maps to unexpected record type {type.Name}");
        }
    }
}
