using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Apps.Builtin;

#nullable enable

/// <summary>
/// The known apps this identity's owner uninstalled (#2138), each with when.  <see cref="BuiltinProvisioner"/>
/// skips them and the drives they own, so an upgrade doesn't bring them back; registering one again takes it off.
/// </summary>
/// <remarks>
/// One row per app, so two uninstalls at once can't overwrite each other's.  An identity without any has uninstalled
/// nothing, so it needs no data upgrade.
/// </remarks>
public class UninstalledBuiltinApps(TableKeyThreeValueCached tblKeyThreeValue)
{
    private static readonly ThreeKeyValueStorage Storage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse("a3f1c6d2-7b84-4e59-9c0a-2d5e8f413b67"));

    private static readonly byte[] DataType = Guid.Parse("5e92b0d4-1c3a-4f78-8d6e-b47a0c29f153").ToByteArray();

    public async Task<IReadOnlySet<Guid>> GetAsync() =>
        (await Storage.GetByDataTypeAsync<UninstalledBuiltinApp>(tblKeyThreeValue, DataType)).Select(a => a.AppId).ToHashSet();

    public Task AddAsync(Guid appId) => Storage.UpsertAsync(tblKeyThreeValue, appId, DataType, DataType,
        new UninstalledBuiltinApp { AppId = appId, UninstalledAt = UnixTimeUtc.Now().milliseconds });

    public Task RemoveAsync(Guid appId) => Storage.DeleteAsync(tblKeyThreeValue, appId);
}

public class UninstalledBuiltinApp
{
    public Guid AppId { get; init; }

    /// <summary>When the owner uninstalled it, in unix ms.</summary>
    public long UninstalledAt { get; init; }
}
