using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Membership.CircleMembership;

/// <summary>
/// Manages circle definitions and their membership.
/// Note, the list of domains in a circle is a cache, the source of truth is with
/// the IdentityConnectionRegistration or YouAuthDomainRegistration.
/// </summary>
public class CircleMembershipService(
    TenantSystemStorage tenantSystemStorage,
    CircleDefinitionService circleDefinitionService,
    ExchangeGrantService exchangeGrantService,
    ILogger<CircleMembershipService> logger)
{
    public void DeleteMemberFromAllCircles(AsciiDomainName domainName, DomainType domainType, DatabaseConnection cn)
    {
        //Note: I updated this to delete by a given domain type so when you login via youauth, your ICR circles are not deleted -_-
        var memberId = OdinId.ToHashId(domainName);
        var circleMemberRecords = tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(cn, memberId);
        cn.CreateCommitUnitOfWork(() =>
        {
            foreach (var circleMemberRecord in circleMemberRecords)
            {
                var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data
                    .ToStringFromUtf8Bytes());
                if (sd.DomainType == domainType)
                {
                    tenantSystemStorage.CircleMemberStorage.Delete(cn, sd.CircleGrant.CircleId, memberId);
                }
            }
        });

        //
        // _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles([OdinId.ToHashId(domainName)]);
    }

    public IEnumerable<CircleGrant> GetCirclesGrantsByDomain(AsciiDomainName domainName, DomainType domainType, DatabaseConnection cn)
    {
        var circleMemberRecords = tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(cn, OdinId.ToHashId(domainName)).Select(d =>
            OdinSystemSerializer.Deserialize<CircleMemberStorageData>(d.data.ToStringFromUtf8Bytes())
        );

        return circleMemberRecords.Where(r => r.DomainType == domainType).Select(r => r.CircleGrant);
    }

    public List<CircleDomainResult> GetDomainsInCircle(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn, bool overrideHack = false)
    {
        //TODO: need to figure out how to enforce this even when the call is
        //coming from EstablishConnection (i.e. we need some form of pre-authorized token)
        if (!overrideHack)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);

            if (SystemCircleConstants.IsSystemCircle(circleId))
            {
                odinContext.Caller.AssertHasMasterKey();
            }
        }

        var memberBytesList = tenantSystemStorage.CircleMemberStorage.GetCircleMembers(cn, circleId);
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

    public void AddCircleMember(Guid circleId, AsciiDomainName domainName, CircleGrant circleGrant, DomainType domainType, DatabaseConnection cn)
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

        // tenantSystemStorage.CircleMemberStorage.Insert(circleMemberRecord);
        tenantSystemStorage.CircleMemberStorage.Upsert(cn, circleMemberRecord);
        // tenantSystemStorage.CircleMemberStorage.UpsertCircleMembers([circleMemberRecord]);
    }

    // Grants

    public async Task<CircleGrant> CreateCircleGrant(SensitiveByteArray keyStoreKey, CircleDefinition def, SensitiveByteArray masterKey,
        IOdinContext odinContext, DatabaseConnection cn)
    {

        if (null == def)
        {
            throw new OdinSystemException("Invalid circle definition");
        }
        
        //map the exchange grant to a structure that matches ICR
        var grant = await exchangeGrantService.CreateExchangeGrant(cn, keyStoreKey, def.Permissions, def.DriveGrants, masterKey, icrKey: null);
        return new CircleGrant()
        {
            CircleId = def.Id,
            KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
            PermissionSet = grant.PermissionSet
        };
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListWithSystemCircle(
        SensitiveByteArray keyStoreKey,
        List<GuidId> circleIds,
        ConnectionRequestOrigin origin,
        SensitiveByteArray masterKey,
        IOdinContext odinContext,
        DatabaseConnection cn)
    {
        var list = CircleNetworkUtils.EnsureSystemCircles(circleIds, origin);
        return await this.CreateCircleGrantList(keyStoreKey, list, masterKey, odinContext, cn);
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(
        SensitiveByteArray keyStoreKey,
        List<GuidId> circleIds,
        SensitiveByteArray masterKey,
        IOdinContext odinContext,
        DatabaseConnection cn)
    {
        var deduplicated = circleIds.Distinct().ToList();

        if (deduplicated.Count() != circleIds.Count())
        {
            logger.LogError("CreateCircleGrantList had duplicate entries. [{circleIds}]", string.Join(",", circleIds));
        }

        var circleGrants = new Dictionary<Guid, CircleGrant>();

        foreach (var id in deduplicated)
        {
            var def = this.GetCircle(id, odinContext, cn);

            if (def == null)
            {
                throw new OdinSystemException($"Missing circle Id {id}");
            }
            
            var cg = await this.CreateCircleGrant(keyStoreKey, def, masterKey, null, cn);

            if (!circleGrants.TryAdd(id.Value, cg))
            {
                logger.LogError("CreateCircleGrantList attempted to insert duplicate key [{keyValue}]", id.Value);
            }
        }

        return circleGrants;
    }

    public (Dictionary<Guid, ExchangeGrant> exchangeGrants, List<GuidId> enabledCircles) MapCircleGrantsToExchangeGrants(List<CircleGrant> circleGrants,
        IOdinContext odinContext, DatabaseConnection cn)
    {
        //TODO: this code needs to be refactored to avoid all the mapping

        // Map CircleGrants to Exchange Grants
        // Note: remember that all connected users are added to a system
        // circle; this circle has grants to all drives marked allowAnonymous == true

        var grants = new Dictionary<Guid, ExchangeGrant>();
        var enabledCircles = new List<GuidId>();
        foreach (var cg in circleGrants)
        {
            if (this.CircleIsEnabled(cg.CircleId, out var circleExists, cn))
            {
                enabledCircles.Add(cg.CircleId);
                grants.Add(cg.CircleId, new ExchangeGrant()
                {
                    Created = 0,
                    Modified = 0,
                    IsRevoked = false, //TODO

                    KeyStoreKeyEncryptedDriveGrants = cg.KeyStoreKeyEncryptedDriveGrants,
                    KeyStoreKeyEncryptedIcrKey = null, //not required since this is not being created for the owner
                    MasterKeyEncryptedKeyStoreKey = null, //not required since this is not being created for the owner
                    PermissionSet = cg.PermissionSet
                });
            }
            else
            {
                if (!circleExists)
                {
                    logger.LogInformation("Caller [{callingIdentity}] has been granted circleId:[{circleId}], which no longer exists",
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
    public async Task CreateCircleDefinition(CreateCircleRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.Create(request, cn);
    }

    /// <summary>
    /// Gets a list of all circle definitions
    /// </summary>
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle, IOdinContext odinContext, DatabaseConnection cn)
    {
        if (includeSystemCircle)
        {
            odinContext.Caller.AssertCallerIsOwner();
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        var circles = await circleDefinitionService.GetCircles(includeSystemCircle, cn);
        return circles;
    }

    public CircleDefinition GetCircle(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        return circleDefinitionService.GetCircle(circleId, cn);
    }

    public async Task AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrants, DatabaseConnection cn)
    {
        await circleDefinitionService.AssertValidDriveGrants(driveGrants, cn);
    }

    public async Task Update(CircleDefinition circleDef, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.Update(circleDef, cn);
    }

    public async Task Delete(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.Delete(circleId, cn);
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    public async Task DisableCircle(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = this.GetCircle(circleId, odinContext, cn);
        circle.Disabled = true;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await circleDefinitionService.Update(circle, cn);
    }

    /// <summary>
    /// Enables a circle
    /// </summary>
    public async Task EnableCircle(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = this.GetCircle(circleId, odinContext, cn);
        circle.Disabled = false;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await circleDefinitionService.Update(circle, cn);
    }

    /// <summary>
    /// Creates the system circle
    /// </summary>
    /// <returns></returns>
    public async Task CreateSystemCircles(IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.CreateSystemCircles(cn);
    }

    private bool CircleIsEnabled(GuidId circleId, out bool exists, DatabaseConnection cn)
    {
        var circle = circleDefinitionService.GetCircle(circleId, cn);
        exists = circle != null;
        return !circle?.Disabled ?? false;
    }
}