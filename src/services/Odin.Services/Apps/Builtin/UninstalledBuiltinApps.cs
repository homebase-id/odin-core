using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Apps.Builtin;

#nullable enable

/// <summary>
/// The provisioned apps this identity's owner uninstalled (#2138), each with when.  <see cref="BuiltinProvisioner"/>
/// skips them and the drives they own, so an upgrade doesn't bring them back; registering one again takes it off.
/// </summary>
/// <remarks>
/// An identity without the record has uninstalled nothing, so it needs no data upgrade.
/// </remarks>
public class UninstalledBuiltinApps(IdentityDatabase db)
{
    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("a3f1c6d2-7b84-4e59-9c0a-2d5e8f413b67"));

    private static readonly Guid RecordKey = Guid.Parse("5e92b0d4-1c3a-4f78-8d6e-b47a0c29f153");

    public async Task<IReadOnlySet<Guid>> GetAsync() => (await GetRecordAsync()).Apps.Keys.ToHashSet();

    public async Task AddAsync(Guid appId)
    {
        var record = await GetRecordAsync();
        if (record.Apps.TryAdd(appId, UnixTimeUtc.Now().milliseconds))
        {
            await Storage.UpsertAsync(db.KeyValueCached, RecordKey, record);
        }
    }

    public async Task RemoveAsync(Guid appId)
    {
        var record = await GetRecordAsync();
        if (record.Apps.Remove(appId))
        {
            await Storage.UpsertAsync(db.KeyValueCached, RecordKey, record);
        }
    }

    private async Task<UninstalledBuiltinAppsRecord> GetRecordAsync() =>
        await Storage.GetAsync<UninstalledBuiltinAppsRecord>(db.KeyValueCached, RecordKey) ?? new UninstalledBuiltinAppsRecord();
}

public class UninstalledBuiltinAppsRecord
{
    /// <summary>App id to when it was uninstalled, in unix ms.</summary>
    public Dictionary<Guid, long> Apps { get; init; } = new();
}
