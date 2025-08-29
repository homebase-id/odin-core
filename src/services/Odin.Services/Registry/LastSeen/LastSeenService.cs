using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Registry.LastSeen;

#nullable enable

public interface ILastSeenService
{
    Dictionary<Guid, UnixTimeUtc> AllByIdentityId { get; }
    Dictionary<string, UnixTimeUtc> AllByDomain { get; }
    void LastSeenNow(IdentityRegistration registration);
    void LastSeenNow(TenantContext tenantContext);
    void LastSeenNow(Guid identityId, string domain);
    void PutLastSeen(IdentityRegistration registration, UnixTimeUtc lastSeen);
    void PutLastSeen(TenantContext tenantContext, UnixTimeUtc lastSeen);
    void PutLastSeen(Guid identityId, string domain, UnixTimeUtc lastSeen);
    void PutLastSeen(RegistrationsRecord? record);
    UnixTimeUtc? GetLastSeen(Guid identityId);
    UnixTimeUtc? GetLastSeen(string domain);
}

//

public class LastSeenService : ILastSeenService
{
    private readonly ConcurrentDictionary<Guid, UnixTimeUtc> _lastSeenByIdentityId = new();
    private readonly ConcurrentDictionary<string, UnixTimeUtc> _lastSeenByDomain = new();

    public Dictionary<Guid, UnixTimeUtc> AllByIdentityId => _lastSeenByIdentityId.ToDictionary();
    public Dictionary<string, UnixTimeUtc> AllByDomain => _lastSeenByDomain.ToDictionary();

    //

    public void LastSeenNow(IdentityRegistration registration)
    {
        PutLastSeen(registration, UnixTimeUtc.Now());
    }

    //

    public void LastSeenNow(TenantContext tenantContext)
    {
        PutLastSeen(tenantContext, UnixTimeUtc.Now());
    }

    //

    public void LastSeenNow(Guid identityId, string domain)
    {
        PutLastSeen(identityId, domain, UnixTimeUtc.Now());
    }

    //

    public void PutLastSeen(IdentityRegistration registration, UnixTimeUtc lastSeen)
    {
        PutLastSeen(registration.Id, registration.PrimaryDomainName, lastSeen);
    }

    //

    public void PutLastSeen(TenantContext tenantContext, UnixTimeUtc lastSeen)
    {
        PutLastSeen(tenantContext.DotYouRegistryId, tenantContext.HostOdinId, lastSeen);
    }

    //

    public void PutLastSeen(Guid identityId, string domain, UnixTimeUtc lastSeen)
    {
        _lastSeenByIdentityId[identityId] = lastSeen;
        _lastSeenByDomain[domain] = lastSeen;
    }

    //

    public void PutLastSeen(RegistrationsRecord? record)
    {
        if (record?.lastSeen != null)
        {
            PutLastSeen(record.identityId, record.primaryDomainName, record.lastSeen.Value);
        }
    }

    //

    public UnixTimeUtc? GetLastSeen(Guid identityId)
    {
        if (_lastSeenByIdentityId.TryGetValue(identityId, out var lastSeen))
        {
            return lastSeen;
        }
        return null;
    }

    //

    public UnixTimeUtc? GetLastSeen(string domain)
    {
        if (_lastSeenByDomain.TryGetValue(domain, out var lastSeen))
        {
            return lastSeen;
        }
        return null;
    }

    //

}
