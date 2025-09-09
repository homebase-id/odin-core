using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Registry;

namespace Odin.Services.LastSeen;

#nullable enable

public interface ILastSeenService
{
    Task LastSeenNowAsync(IdentityRegistration registration);
    Task LastSeenNowAsync(TenantContext tenantContext);
    Task LastSeenNowAsync(OdinIdentity odinIdentity);
    Task LastSeenNowAsync(Guid identityId);
    Task PutLastSeenAsync(IdentityRegistration registration, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(TenantContext tenantContext, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(OdinIdentity odinIdentity, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(Guid identityId, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(LastSeenRecord record);
    Task<UnixTimeUtc?> GetLastSeenAsync(Guid identityId);
}
