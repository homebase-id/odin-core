using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Registry;

namespace Odin.Services.LastSeen;

#nullable enable

public interface ILastSeenService
{
    Task LastSeenNowAsync(IdentityRegistration registration);
    Task LastSeenNowAsync(TenantContext tenantContext);
    Task LastSeenNowAsync(IOdinContext odinContext);
    Task LastSeenNowAsync(OdinIdentity odinIdentity);
    Task LastSeenNowAsync(OdinId odinId);
    Task LastSeenNowAsync(string domain);
    Task PutLastSeenAsync(IdentityRegistration registration, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(TenantContext tenantContext, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(IOdinContext odinContext, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(OdinIdentity odinIdentity, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(OdinId odinId, UnixTimeUtc lastSeen);
    Task PutLastSeenAsync(string domain, UnixTimeUtc lastSeen);
    Task<UnixTimeUtc?> GetLastSeenAsync(OdinId odinId);
    Task<UnixTimeUtc?> GetLastSeenAsync(IOdinContext odinContext);
    Task<UnixTimeUtc?> GetLastSeenAsync(string domain);
}
