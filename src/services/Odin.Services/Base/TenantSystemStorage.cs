using System;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Services.Base;

// SEB:TODO delete class TenantSystemStorage?
public static class TenantSystemStorage
{
    public static SingleKeyValueStorage CreateSingleKeyValueStorage(Guid contextKey)
    {
        return new SingleKeyValueStorage(contextKey);
    }

    public static TwoKeyValueStorage CreateTwoKeyValueStorage(Guid contextKey)
    {
        return new TwoKeyValueStorage(contextKey);
    }

    /// <summary>
    /// Store values using a single key while offering 2 other keys to categorize your data
    /// </summary>
    /// <param name="contextKey">Will be combined with the key to ensure unique storage in the TblKeyThreeValue table</param>
    public static ThreeKeyValueStorage CreateThreeKeyValueStorage(Guid contextKey)
    {
        return new ThreeKeyValueStorage(contextKey);
    }
}
