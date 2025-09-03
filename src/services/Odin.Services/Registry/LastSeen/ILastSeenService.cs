using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Registry.LastSeen;

#nullable enable

public interface ILastSeenService
{
    Task LastSeenNowAsync(IdentityRegistration registration);
    Task LastSeenNowAsync(TenantContext tenantContext);
    Task LastSeenNowAsync(OdinIdentity odinIdentity);
    Task LastSeenNowAsync(Guid identityId, string domain);
    Task PutLastSeenAsync(IdentityRegistration registration, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(TenantContext tenantContext, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(OdinIdentity odinIdentity, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(Guid identityId, string domain, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(RegistrationsRecord? record);
    Task<UnixTimeUtc?> GetLastSeenAsync(Guid identityId);
    Task<UnixTimeUtc?> GetLastSeenAsync(string domain);
}
