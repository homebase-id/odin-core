using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

/// <summary>A header write whose tags didn't change leaves the file's tag rows alone (#1961).</summary>
public class MainIndexMetaTagRowsTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task AHeaderWriteWithTheSameTags_KeepsTheTagRows(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var metaIndex = scope.Resolve<MainIndexMeta>();
        var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
        var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

        var (driveId, fileId) = (Guid.NewGuid(), Guid.NewGuid());
        var tags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, fileId, Guid.NewGuid(), 1, 1, "s", null, null, 1,
            UnixTimeUtc.Now(), 1, null, tags, 1);

        async Task<Dictionary<Guid, long>> RowIdsAsync()
        {
            var (rows, _) = await tblDriveTagIndex.PagingByRowIdAsync(100, null);
            return rows.Where(r => r.fileId == fileId).ToDictionary(r => r.tagId, r => r.rowId);
        }

        var before = await RowIdsAsync();
        Assert.That(before.Keys, Is.EquivalentTo(tags));

        var record = await tblDriveMainIndex.GetAsync(driveId, fileId);
        await metaIndex.BaseUpsertEntryZapZapAsync(record, accessControlList: null, tagIdList: tags);

        Assert.That(await RowIdsAsync(), Is.EquivalentTo(before), "the same tags: no tag row rewritten");
    }
}
