using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Membership.CircleMembership;

/// <summary>
/// Manages circle definitions and their membership.
/// Note, the list of domains in a circle is a cache, the source of truth is with
/// the IdentityConnectionRegistration or YouAuthDomainRegistration.
/// </summary>
public class CircleMembershipService
{
    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly CircleDefinitionService _circleDefinitionService;
    private readonly ExchangeGrantService _exchangeGrantService;
    private readonly ILogger<CircleMembershipService> _logger;

    public CircleMembershipService(TenantSystemStorage tenantSystemStorage, CircleDefinitionService circleDefinitionService,
        ExchangeGrantService exchangeGrantService, ILogger<CircleMembershipService> logger)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleDefinitionService = circleDefinitionService;
        _exchangeGrantService = exchangeGrantService;

        _logger = logger;
    }

    public void DeleteMemberFromAllCircles(AsciiDomainName domainName, DomainType domainType)
    {
        //Note: I updated this to delete by a given domain type so when you login via youauth, your ICR circles are not deleted -_-
        var memberId = OdinId.ToHashId(domainName);
        using var cn = _tenantSystemStorage.CreateConnection();
        var circleMemberRecords = _tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(cn, memberId);
        using (cn.CreateCommitUnitOfWork())
        {
            foreach (var circleMemberRecord in circleMemberRecords)
            {
                var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data.ToStringFromUtf8Bytes());
                if (sd.DomainType == domainType)
                {
                    _tenantSystemStorage.CircleMemberStorage.Delete(cn, sd.CircleGrant.CircleId, memberId);
                }
            }
        }

        //
        // _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles([OdinId.ToHashId(domainName)]);
    }

    public IEnumerable<CircleGrant> GetCirclesGrantsByDomain(AsciiDomainName domainName, DomainType domainType)
    {
        using var cn = _tenantSystemStorage.CreateConnection();
        var circleMemberRecords = _tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(cn, OdinId.ToHashId(domainName)).Select(d =>
            OdinSystemSerializer.Deserialize<CircleMemberStorageData>(d.data.ToStringFromUtf8Bytes())
        );

        return circleMemberRecords.Where(r => r.DomainType == domainType).Select(r => r.CircleGrant);
    }

    public List<CircleDomainResult> GetDomainsInCircle(GuidId circleId, IOdinContext odinContext, bool overrideHack = false)
    {
        //TODO: need to figure out how to enforce this even when the call is
        //coming from EstablishConnection (i.e. we need some form of pre-authorized token)
        if (!overrideHack)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);

            if (circleId == SystemCircleConstants.ConnectedIdentitiesSystemCircleId)
            {
                odinContext.Caller.AssertHasMasterKey();
            }
        }

        using var cn = _tenantSystemStorage.CreateConnection();
        var memberBytesList = _tenantSystemStorage.CircleMemberStorage.GetCircleMembers(cn, circleId);
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

        using var cn = _tenantSystemStorage.CreateConnection();
        _tenantSystemStorage.CircleMemberStorage.UpsertCircleMembers(cn, new List<CircleMemberRecord>() { circleMemberRecord });
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

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListWithSystemCircle(List<GuidId> circleIds, SensitiveByteArray keyStoreKey,
        IOdinContext odinContext)
    {
        // Always put identities in the system circle
        var list = circleIds ?? new List<GuidId>();
        list.Add(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
        return await this.CreateCircleGrantList(list, keyStoreKey, odinContext);
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey, IOdinContext odinContext)
    {
        var masterKey = odinContext.Caller.GetMasterKey();

        var deduplicated = circleIds.Distinct().ToList();

        if (deduplicated.Count() != circleIds.Count())
        {
            _logger.LogError("CreateCircleGrantList had duplicate entries. [{circleIds}]", string.Join(",", circleIds));
        }

        var circleGrants = new Dictionary<Guid, CircleGrant>();

        foreach (var id in deduplicated)
        {
            var def = this.GetCircle(id, odinContext);
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

    public (Dictionary<Guid, ExchangeGrant> exchangeGrants, List<GuidId> enabledCircles) MapCircleGrantsToExchangeGrants(List<CircleGrant> circleGrants,
        IOdinContext odinContext)
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
                        odinContext.Caller.OdinId, cg.CircleId);
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
    public async Task CreateCircleDefinition(CreateCircleRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await _circleDefinitionService.Create(request);
    }

    /// <summary>
    /// Gets a list of all circle definitions
    /// </summary>
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle, IOdinContext odinContext)
    {
        if (includeSystemCircle)
        {
            odinContext.Caller.AssertHasMasterKey();
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        var circles = await _circleDefinitionService.GetCircles(includeSystemCircle);
        return circles;
    }

    public CircleDefinition GetCircle(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        return _circleDefinitionService.GetCircle(circleId);
    }

    public async Task AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrants)
    {
        await _circleDefinitionService.AssertValidDriveGrants(driveGrants);
    }

    public async Task Update(CircleDefinition circleDef, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await _circleDefinitionService.Update(circleDef);
    }

    public async Task Delete(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await _circleDefinitionService.Delete(circleId);
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    /// <param name="circleId"></param>
    public async Task DisableCircle(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = this.GetCircle(circleId, odinContext);
        circle.Disabled = true;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await _circleDefinitionService.Update(circle);
    }

    /// <summary>
    /// Enables a circle
    /// </summary>
    /// <param name="circleId"></param>
    public async Task EnableCircle(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = this.GetCircle(circleId, odinContext);
        circle.Disabled = false;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await _circleDefinitionService.Update(circle);
    }

    /// <summary>
    /// Creates the system circle
    /// </summary>
    /// <returns></returns>
    public async Task CreateSystemCircle(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await _circleDefinitionService.CreateSystemCircle();
    }

    private bool CircleIsEnabled(GuidId circleId, out bool exists)
    {
        var circle = _circleDefinitionService.GetCircle(circleId);
        exists = circle != null;
        return !circle?.Disabled ?? false;
    }
}