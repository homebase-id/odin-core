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
public class CircleMembershipService(
    TenantSystemStorage tenantSystemStorage,
    CircleDefinitionService circleDefinitionService,
    ExchangeGrantService exchangeGrantService,
    ILogger<CircleMembershipService> logger)
{
    public void DeleteMemberFromAllCircles(AsciiDomainName domainName, DomainType domainType)
    {
        //Note: I updated this to delete by a given domain type so when you login via youauth, your ICR circles are not deleted -_-
        var memberId = OdinId.ToHashId(domainName);
        var circleMemberRecords = tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(memberId);

        // TODO CONNECTIONS
        //db.CreateCommitUnitOfWork(() => {
        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data
                .ToStringFromUtf8Bytes());
            if (sd.DomainType == domainType)
            {
                tenantSystemStorage.CircleMemberStorage.Delete(sd.CircleGrant.CircleId, memberId);
            }
        }
        // }); TODO CONNECTIONS

        //
        // _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles([OdinId.ToHashId(domainName)]);
    }

    public IEnumerable<CircleGrant> GetCirclesGrantsByDomain(AsciiDomainName domainName, DomainType domainType)
    {
        var circleMemberRecords = tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(OdinId.ToHashId(domainName)).Select(d =>
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

        var memberBytesList = tenantSystemStorage.CircleMemberStorage.GetCircleMembers(circleId);
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

        // tenantSystemStorage.CircleMemberStorage.Insert(circleMemberRecord);
        tenantSystemStorage.CircleMemberStorage.Upsert(circleMemberRecord);
        // tenantSystemStorage.CircleMemberStorage.UpsertCircleMembers([circleMemberRecord]);
    }

    // Grants

    public async Task<CircleGrant> CreateCircleGrant(CircleDefinition def, SensitiveByteArray keyStoreKey, SensitiveByteArray masterKey,
        IOdinContext odinContext, IdentityDatabase db)
    {
        //map the exchange grant to a structure that matches ICR
        var grant = await exchangeGrantService.CreateExchangeGrant(db, keyStoreKey, def.Permissions, def.DriveGrants, masterKey, icrKey: null);
        return new CircleGrant()
        {
            CircleId = def.Id,
            KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
            PermissionSet = grant.PermissionSet
        };
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListWithSystemCircle(List<GuidId> circleIds, SensitiveByteArray keyStoreKey,
        IOdinContext odinContext, IdentityDatabase db)
    {
        // Always put identities in the system circle
        var list = circleIds ?? new List<GuidId>();
        list.Add(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
        return await this.CreateCircleGrantList(list, keyStoreKey, odinContext, db);
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey, IOdinContext odinContext,
        IdentityDatabase db)
    {
        var masterKey = odinContext.Caller.GetMasterKey();

        var deduplicated = circleIds.Distinct().ToList();

        if (deduplicated.Count() != circleIds.Count())
        {
            logger.LogError("CreateCircleGrantList had duplicate entries. [{circleIds}]", string.Join(",", circleIds));
        }

        var circleGrants = new Dictionary<Guid, CircleGrant>();

        foreach (var id in deduplicated)
        {
            var def = this.GetCircle(id, odinContext);
            var cg = await this.CreateCircleGrant(def, keyStoreKey, masterKey, null, db);

            if (circleGrants.ContainsKey(id.Value))
            {
                logger.LogError("CreateCircleGrantList attempted to insert duplicate key [{keyValue}]", id.Value);
            }
            else
            {
                circleGrants.Add(id.Value, cg);
            }
        }

        return circleGrants;
    }

    public (Dictionary<Guid, ExchangeGrant> exchangeGrants, List<GuidId> enabledCircles) MapCircleGrantsToExchangeGrants(AsciiDomainName domainName,
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
            if (this.CircleIsEnabled(cg.CircleId, out var circleExists, tenantSystemStorage.IdentityDatabase))
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

        if (logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var grant in grants.Values)
            {
                var redacted = grant.Redacted();
                var dg = redacted.DriveGrants == null ? "none" : string.Join("\n", redacted.DriveGrants);
                logger.LogDebug("domain name (caller) {callingIdentity} granted drives: \n{g}", domainName, dg);
            }
        }

        return (grants, enabledCircles);
    }

    // Definitions

    /// <summary>
    /// Creates a circle definition
    /// </summary>
    public async Task CreateCircleDefinition(CreateCircleRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.Create(request);
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
        var circles = await circleDefinitionService.GetCircles(includeSystemCircle);
        return circles;
    }

    public CircleDefinition GetCircle(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        return circleDefinitionService.GetCircle(circleId);
    }

    public async Task AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrants)
    {
        await circleDefinitionService.AssertValidDriveGrants(driveGrants);
    }

    public async Task Update(CircleDefinition circleDef, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.Update(circleDef);
    }

    public async Task Delete(GuidId circleId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.Delete(circleId);
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    public async Task DisableCircle(GuidId circleId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = this.GetCircle(circleId, odinContext);
        circle.Disabled = true;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await circleDefinitionService.Update(circle);
    }

    /// <summary>
    /// Enables a circle
    /// </summary>
    public async Task EnableCircle(GuidId circleId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();

        var circle = this.GetCircle(circleId, odinContext);
        circle.Disabled = false;
        circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
        await circleDefinitionService.Update(circle);
    }

    /// <summary>
    /// Creates the system circle
    /// </summary>
    /// <returns></returns>
    public async Task CreateSystemCircle(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await circleDefinitionService.CreateSystemCircle();
    }

    private bool CircleIsEnabled(GuidId circleId, out bool exists, IdentityDatabase db)
    {
        var circle = circleDefinitionService.GetCircle(circleId);
        exists = circle != null;
        return !circle?.Disabled ?? false;
    }
}