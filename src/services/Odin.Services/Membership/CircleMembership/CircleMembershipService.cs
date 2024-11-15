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
    public async Task DeleteMemberFromAllCirclesAsync(AsciiDomainName domainName, DomainType domainType)
    {
        //Note: I updated this to delete by a given domain type so when you login via youauth, your ICR circles are not deleted -_-
        var memberId = OdinId.ToHashId(domainName);
        var circleMemberRecords = await tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndDataAsync(memberId);

        // TODO CONNECTIONS
        //db.CreateCommitUnitOfWork(() => {
        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data
                .ToStringFromUtf8Bytes());
            if (sd.DomainType == domainType)
            {
                await tenantSystemStorage.CircleMemberStorage.DeleteAsync(sd.CircleGrant.CircleId, memberId);
            }
        }
        // }); TODO CONNECTIONS

    }

    public async Task<IEnumerable<CircleGrant>> GetCirclesGrantsByDomainAsync(AsciiDomainName domainName, DomainType domainType)
    {
        var records =
            await tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndDataAsync(OdinId.ToHashId(domainName));
        var circleMemberRecords = records.Select(d =>
            OdinSystemSerializer.Deserialize<CircleMemberStorageData>(d.data.ToStringFromUtf8Bytes())
        );
        return circleMemberRecords.Where(r => r.DomainType == domainType).Select(r => r.CircleGrant);
    }

    public async Task<List<CircleDomainResult>> GetDomainsInCircleAsync(GuidId circleId, IOdinContext odinContext, bool overrideHack = false)
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

        var memberBytesList = await tenantSystemStorage.CircleMemberStorage.GetCircleMembersAsync(circleId);
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

    public async Task AddCircleMemberAsync(Guid circleId, AsciiDomainName domainName, CircleGrant circleGrant, DomainType domainType)
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
        await tenantSystemStorage.CircleMemberStorage.UpsertAsync(circleMemberRecord);
        // tenantSystemStorage.CircleMemberStorage.UpsertCircleMembers([circleMemberRecord]);
    }

    // Grants

    public async Task<CircleGrant> CreateCircleGrantAsync(SensitiveByteArray keyStoreKey, CircleDefinition def, SensitiveByteArray masterKey,
        IOdinContext odinContext)
    {
        var db = tenantSystemStorage.IdentityDatabase;

        if (null == def)
        {
            throw new OdinSystemException("Invalid circle definition");
        }

        //map the exchange grant to a structure that matches ICR
        var grant = await exchangeGrantService.CreateExchangeGrantAsync(db, keyStoreKey, def.Permissions, def.DriveGrants, masterKey,
            icrKey: null);
        return new CircleGrant()
        {
            CircleId = def.Id,
            KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
            PermissionSet = grant.PermissionSet
        };
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListWithSystemCircleAsync(
        SensitiveByteArray keyStoreKey,
        List<GuidId> circleIds,
        ConnectionRequestOrigin origin,
        SensitiveByteArray masterKey,
        IOdinContext odinContext)
    {
        var list = CircleNetworkUtils.EnsureSystemCircles(circleIds, origin);
        return await this.CreateCircleGrantListAsync(keyStoreKey, list, masterKey, odinContext);
    }


    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListAsync(
        SensitiveByteArray keyStoreKey,
        List<GuidId> circleIds,
        SensitiveByteArray masterKey,
        IOdinContext odinContext)
    {
        var deduplicated = circleIds.Distinct().ToList();

        if (deduplicated.Count() != circleIds.Count())
        {
            logger.LogError("CreateCircleGrantList had duplicate entries. [{circleIds}]", string.Join(",", circleIds));
        }

        var circleGrants = new Dictionary<Guid, CircleGrant>();

        foreach (var id in deduplicated)
        {
            var def = await GetCircleAsync(id, odinContext);

            if (def == null)
            {
                throw new OdinSystemException($"Missing circle Id {id}");
            }

            var cg = await this.CreateCircleGrantAsync(keyStoreKey, def, masterKey, null);


            if (!circleGrants.TryAdd(id.Value, cg))
            {
                logger.LogError("CreateCircleGrantList attempted to insert duplicate key [{keyValue}]", id.Value);
            }
        }

        return circleGrants;
    }

    public async Task<(Dictionary<Guid, ExchangeGrant> exchangeGrants, List<GuidId> enabledCircles)> MapCircleGrantsToExchangeGrantsAsync(
        AsciiDomainName domainName,
        List<CircleGrant> circleGrants,
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
            var (enabled, exists) = await CircleIsEnabledAsync(cg.CircleId);             
            if (enabled)
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
                if (!exists)
                {
                    logger.LogInformation("Caller [{callingIdentity}] has been granted circleId:[{circleId}], which no longer exists",
                        odinContext.Caller.OdinId, cg.CircleId);
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var grant in grants.Values)
            {
                var redacted = grant.Redacted();
                var dg = redacted.DriveGrants == null ? "none" : string.Join("|", redacted.DriveGrants);
                logger.LogDebug("domain name (caller) {callingIdentity} granted drives: [{g}]", domainName, dg);
            }
        }

        return (grants, enabledCircles);
    }

    // Definitions

    /// <summary>
    /// Creates a circle definition
    /// </summary>
    public async Task CreateCircleDefinitionAsync(CreateCircleRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await circleDefinitionService.CreateAsync(request);
    }

    /// <summary>
    /// Gets a list of all circle definitions
    /// </summary>
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle, IOdinContext odinContext)
    {
        if (includeSystemCircle)
        {
            odinContext.Caller.AssertCallerIsOwner();
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        var circles = await circleDefinitionService.GetCirclesAsync(includeSystemCircle);
        return circles;
    }

    public async Task<CircleDefinition> GetCircleAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        return await circleDefinitionService.GetCircleAsync(circleId);
    }

    public async Task AssertValidDriveGrantsAsync(IEnumerable<DriveGrantRequest> driveGrants)
    {
        await circleDefinitionService.AssertValidDriveGrantsAsync(driveGrants);
    }

    public async Task UpdateAsync(CircleDefinition circleDef, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.UpdateAsync(circleDef);
    }

    public async Task DeleteAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.DeleteAsync(circleId);
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    public async Task DisableCircleAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = await this.GetCircleAsync(circleId, odinContext);
        circle.Disabled = true;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await circleDefinitionService.UpdateAsync(circle);
    }

    /// <summary>
    /// Enables a circle
    /// </summary>
    public async Task EnableCircleAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = await this.GetCircleAsync(circleId, odinContext);
        circle.Disabled = false;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await circleDefinitionService.UpdateAsync(circle);
    }

    /// <summary>
    /// Creates the system circle
    /// </summary>
    /// <returns></returns>
    public async Task CreateSystemCirclesAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await circleDefinitionService.EnsureSystemCirclesExistAsync();
    }

    private async Task<(bool enabled, bool exists)> CircleIsEnabledAsync(GuidId circleId)
    {
        var circle = await circleDefinitionService.GetCircleAsync(circleId);
        var exists = circle != null;
        var enabled = exists && !circle.Disabled;
        return (enabled, exists);
    }
}