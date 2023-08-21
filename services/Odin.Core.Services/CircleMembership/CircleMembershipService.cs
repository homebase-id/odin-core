using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.Contacts.Circle.Membership.Definition;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Services.CircleMembership;

/// <summary>
/// Manages circle definitions and their membership.
/// Note, the list of domains in a circle is a cache, the source of truth is with
/// the IdentityConnectionRegistration or YouAuthDomainRegistration.
/// </summary>
public class CircleMembershipService
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly CircleDefinitionService _circleDefinitionService;
    private readonly ExchangeGrantService _exchangeGrantService;

    public CircleMembershipService(TenantSystemStorage tenantSystemStorage, CircleDefinitionService circleDefinitionService, ExchangeGrantService exchangeGrantService, OdinContextAccessor contextAccessor)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleDefinitionService = circleDefinitionService;
        _exchangeGrantService = exchangeGrantService;
        _contextAccessor = contextAccessor;
    }

    public void DeleteMemberFromAllCircles(OdinId odinId)
    {
        _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles(new List<Guid>() { odinId.ToHashId() });
    }

    public void DeleteMembersFromAllCircles(List<OdinId> domains)
    {
        _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles(domains.Select(d => d.ToHashId()).ToList());
    }

    public IEnumerable<CircleGrant> GetCirclesByOdinId(OdinId odinId)
    {
        var circleMemberRecords = _tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(odinId.ToHashId());
        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data.ToStringFromUtf8Bytes());
            yield return sd.CircleGrant;
        }
    }

    public List<OdinId> GetCircleMembers(GuidId circleId)
    {
        //Note: this list is a cache of members for a circle.  the source of truth is the
        //IdentityConnectionRegistration.AccessExchangeGrant.CircleGrants property for each OdinId
        var memberBytesList = _tenantSystemStorage.CircleMemberStorage.GetCircleMembers(circleId);
        var result = memberBytesList.Select(item =>
        {
            var data = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(item.data.ToStringFromUtf8Bytes());
            return data.OdinId;
        }).ToList();

        return result;
    }

    public void AddCircleMember(Guid circleId, OdinId odinId, CircleGrant circleGrant)
    {
        var circleMemberRecord = new CircleMemberRecord()
        {
            circleId = circleId,
            memberId = odinId.ToHashId(),
            data = OdinSystemSerializer.Serialize(new CircleMemberStorageData
            {
                OdinId = odinId,
                CircleGrant = circleGrant
            }).ToUtf8ByteArray()
        };

        _tenantSystemStorage.CircleMemberStorage.AddCircleMembers(new List<CircleMemberRecord>() { circleMemberRecord });
    }

    public async Task<CircleGrant> CreateCircleGrant(CircleDefinition def, SensitiveByteArray keyStoreKey, SensitiveByteArray masterKey)
    {
        //map the exchange grant to a structure that matches ICR
        var grant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, def.Permissions, def.DriveGrants, masterKey);
        return new CircleGrant()
        {
            CircleId = def.Id,
            KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
            PermissionSet = grant.PermissionSet,
        };
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey)
    {
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

        var circleGrants = new Dictionary<Guid, CircleGrant>();

        // Always put identities in the system circle
        var list = circleIds ?? new List<GuidId>();
        list.Add(CircleConstants.SystemCircleId);

        foreach (var id in list)
        {
            var def = this.GetCircle(id);

            var cg = await this.CreateCircleGrant(def, keyStoreKey, masterKey);
            circleGrants.Add(id.Value, cg);
        }

        return circleGrants;
    }

    
    // Definitions

    /// <summary>
    /// Creates a circle definition
    /// </summary>
    /// <param name="request"></param>
    public async Task CreateCircleDefinition(CreateCircleRequest request)
    {
        await _circleDefinitionService.Create(request);
    }

    /// <summary>
    /// Gets a list of all circle definitions
    /// </summary>
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
    {
        var circles = await _circleDefinitionService.GetCircles(includeSystemCircle);
        return circles;
    }

    public CircleDefinition GetCircle(GuidId circleId)
    {
        return _circleDefinitionService.GetCircle(circleId);
    }

    public void AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrants)
    {
        _circleDefinitionService.AssertValidDriveGrants(driveGrants);
    }

    public async Task Update(CircleDefinition circleDef)
    {
        await _circleDefinitionService.Update(circleDef);
    }

    public async Task Delete(GuidId circleId)
    {
        await _circleDefinitionService.Delete(circleId);
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    /// <param name="circleId"></param>
    public Task DisableCircle(GuidId circleId)
    {
        var circle = this.GetCircle(circleId);
        circle.Disabled = true;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        _circleDefinitionService.Update(circle);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Enables a circle
    /// </summary>
    /// <param name="circleId"></param>
    public Task EnableCircle(GuidId circleId)
    {
        var circle = this.GetCircle(circleId);
        circle.Disabled = false;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        _circleDefinitionService.Update(circle);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the system circle
    /// </summary>
    /// <returns></returns>
    public Task CreateSystemCircle()
    {
        _circleDefinitionService.CreateSystemCircle();
        return Task.CompletedTask;
    }

    public bool IsEnabled(GuidId circleId)
    {
        return _circleDefinitionService.IsEnabled(circleId);
    }
    
}