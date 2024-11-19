using System;
using System.IO;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.SQLite;
using Odin.Core.Util;

namespace Odin.Services.Base;

// SEB:TODO delete class TenantSystemStorage?
public sealed class TenantSystemStorage
{
    public TenantSystemStorage(TenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(tenantContext.StorageConfig);

        var dbPath = tenantContext.StorageConfig.HeaderDataStoragePath;
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }
    }

    public SingleKeyValueStorage CreateSingleKeyValueStorage(Guid contextKey)
    {
        return new SingleKeyValueStorage(contextKey);
    }

    public TwoKeyValueStorage CreateTwoKeyValueStorage(Guid contextKey)
    {
        return new TwoKeyValueStorage(contextKey);
    }

    /// <summary>
    /// Store values using a single key while offering 2 other keys to categorize your data
    /// </summary>
    /// <param name="contextKey">Will be combined with the key to ensure unique storage in the TblKeyThreeValue table</param>
    public ThreeKeyValueStorage CreateThreeKeyValueStorage(Guid contextKey)
    {
        return new ThreeKeyValueStorage(contextKey);
    }
}
