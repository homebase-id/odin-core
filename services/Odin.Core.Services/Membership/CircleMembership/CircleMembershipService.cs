using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.CircleMembership;

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
    private readonly ILogger<CircleMembershipService> _logger;

    public CircleMembershipService(TenantSystemStorage tenantSystemStorage, CircleDefinitionService circleDefinitionService,
        ExchangeGrantService exchangeGrantService, OdinContextAccessor contextAccessor, ILogger<CircleMembershipService> logger)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleDefinitionService = circleDefinitionService;
        _exchangeGrantService = exchangeGrantService;
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    public void DeleteMemberFromAllCircles(AsciiDomainName domainName)
    {
        _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles(new List<Guid>() { OdinId.ToHashId(domainName) });
    }

    public IEnumerable<CircleGrant> GetCirclesGrantsByDomain(AsciiDomainName domainName)
    {
        var circleMemberRecords = _tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(OdinId.ToHashId(domainName));
        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data.ToStringFromUtf8Bytes());
            yield return sd.CircleGrant;
        }
    }

    public List<CircleDomainResult> GetDomainsInCircle(GuidId circleId, bool overrideHack = false)
    {
        //TODO: need to figure out how to enforce this even when the call is
        //coming from EstablishConnection (i.e. we need some form of pre-authorized token)
        if (!overrideHack)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);

            if (circleId == SystemCircleConstants.ConnectedIdentitiesSystemCircleId)
            {
                _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            }
        }

        var memberBytesList = _tenantSystemStorage.CircleMemberStorage.GetCircleMembers(circleId);
        var result = memberBytesList.Select(item =>
        {
            var data = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(item.data.ToStringFromUtf8Bytes());
            return new CircleDomainResult()
            {
                DomainType = data.DomainType,
                Domain = data.DomainName,
                CircleGrant = data.CircleGrant.Redacted()
            };
        }).ToList();

        return result;
    }

    public void AddCircleMember(Guid circleId, AsciiDomainName domainName, CircleGrant circleGrant, DomainType domainType)
    {
        var circleMemberRecord = new CircleMemberRecord()
        {
            circleId = circleId,
            memberId = OdinId.ToHashId(domainName),
            data = OdinSystemSerializer.Serialize(new CircleMemberStorageData
            {
                DomainType = domainType,
                DomainName = domainName,
                CircleGrant = circleGrant
            }).ToUtf8ByteArray()
        };

        _tenantSystemStorage.CircleMemberStorage.UpsertCircleMembers(new List<CircleMemberRecord>() { circleMemberRecord });
    }

    // Grants

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

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListWithSystemCircle(List<GuidId> circleIds, SensitiveByteArray keyStoreKey)
    {
        // Always put identities in the system circle
        var list = circleIds ?? new List<GuidId>();
        list.Add(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
        return await this.CreateCircleGrantList(list, keyStoreKey);
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey)
    {
        var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

        var deduplicated = circleIds.Distinct().ToList();

        if (deduplicated.Count() != circleIds.Count())
        {
            _logger.LogError("CreateCircleGrantList had duplicate entries. [{circleIds}]", string.Join(",", circleIds));
        }

        var circleGrants = new Dictionary<Guid, CircleGrant>();

        foreach (var id in deduplicated)
        {
            var def = this.GetCircle(id);
            var cg = await this.CreateCircleGrant(def, keyStoreKey, masterKey);

            if (circleGrants.ContainsKey(id.Value))
            {
                _logger.LogError("CreateCircleGrantList attempted to insert duplicate key [{keyValue}]", id.Value);
            }
            else
            {
                circleGrants.Add(id.Value, cg);
            }
        }

        return circleGrants;
    }

    public (Dictionary<Guid, ExchangeGrant> exchangeGrants, List<GuidId> enabledCircles) MapCircleGrantsToExchangeGrants(List<CircleGrant> circleGrants)
    {
        //TODO: this code needs to be refactored to avoid all the mapping

        // Map CircleGrants to Exchange Grants
        // Note: remember that all connected users are added to a system
        // circle; this circle has grants to all drives marked allowAnonymous == true

        var grants = new Dictionary<Guid, ExchangeGrant>();
        var enabledCircles = new List<GuidId>();
        foreach (var cg in circleGrants)
        {
            if (this.CircleIsEnabled(cg.CircleId, out var circleExists))
            {
                enabledCircles.Add(cg.CircleId);
                grants.Add(cg.CircleId, new ExchangeGrant()
                {
                    Created = 0,
                    Modified = 0,
                    IsRevoked = false, //TODO

                    KeyStoreKeyEncryptedDriveGrants = cg.KeyStoreKeyEncryptedDriveGrants,
                    KeyStoreKeyEncryptedIcrKey = null, // not allowed to use the icr CAT because you're not sending over
                    MasterKeyEncryptedKeyStoreKey = null, //not required since this is not being created for the owner
                    PermissionSet = cg.PermissionSet
                });
            }
            else
            {
                if (!circleExists)
                {
                    _logger.LogInformation("Caller [{callingIdentity}] has been granted circleId:[{circleId}], which no longer exists",
                        _contextAccessor.GetCurrent().Caller.OdinId, cg.CircleId);
                }
            }
        }

        return (grants, enabledCircles);
    }

    // Definitions

    /// <summary>
    /// Creates a circle definition
    /// </summary>
    /// <param name="request"></param>
    public async Task CreateCircleDefinition(CreateCircleRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        await _circleDefinitionService.Create(request);
    }

    /// <summary>
    /// Gets a list of all circle definitions
    /// </summary>
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
    {
        if (includeSystemCircle)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        }

        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        var circles = await _circleDefinitionService.GetCircles(includeSystemCircle);
        return circles;
    }

    public CircleDefinition GetCircle(GuidId circleId)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        return _circleDefinitionService.GetCircle(circleId);
    }

    public void AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrants)
    {
        _circleDefinitionService.AssertValidDriveGrants(driveGrants);
    }

    public async Task Update(CircleDefinition circleDef)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        await _circleDefinitionService.Update(circleDef);
    }

    public async Task Delete(GuidId circleId)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        await _circleDefinitionService.Delete(circleId);
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    /// <param name="circleId"></param>
    public Task DisableCircle(GuidId circleId)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

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
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

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
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        _circleDefinitionService.CreateSystemCircle();
        return Task.CompletedTask;
    }

    private bool CircleIsEnabled(GuidId circleId, out bool exists)
    {
        var circle = _circleDefinitionService.GetCircle(circleId);
        exists = circle != null;
        return !circle?.Disabled ?? false;
    }
}