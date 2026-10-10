using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.Apps;
using Odin.Services.Apps;
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
    CircleDefinitionService circleDefinitionService,
    ExchangeGrantService exchangeGrantService,
    ILogger<CircleMembershipService> logger,
    IMediator mediator,
    IdentityDatabase db,
    TenantContext tenantContext)
{
    public async Task Temp_ReconcileCircleAndAppGrants()
    {
        await using var tx = await db.BeginStackedTransactionAsync();

        logger.LogInformation("Migrating Circle Grants");

        var allCircleMembers = await db.CircleMemberCached.GetAllCirclesAsync();
        foreach (var cmr in allCircleMembers)
        {
            var storageData = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(cmr.data.ToStringFromUtf8Bytes());

            //convert the driveId to the alias
            var updatedDriveIdList = storageData.CircleGrant.KeyStoreKeyEncryptedDriveGrants.Select(g => new DriveGrant
            {
                DriveId = g.PermissionedDrive.Drive.Alias,
                PermissionedDrive = g.PermissionedDrive, // keep for now
                KeyStoreKeyEncryptedStorageKey = g.KeyStoreKeyEncryptedStorageKey
            });
            
            storageData.CircleGrant.KeyStoreKeyEncryptedDriveGrants = updatedDriveIdList.ToList();
            cmr.data = OdinSystemSerializer.Serialize(storageData).ToUtf8ByteArray();
            await db.CircleMemberCached.UpsertAsync(cmr);
        }

        logger.LogInformation("Migrating App Grants");
        var allAppGrants = await db.AppGrantsCached.GetAllAsync();
        foreach (var appGrant in allAppGrants)
        {
            var storageData = OdinSystemSerializer.Deserialize<AppCircleGrant>(appGrant.data.ToStringFromUtf8Bytes());
            
            var updatedAppGrantDriveIdList = storageData.KeyStoreKeyEncryptedDriveGrants.Select(g => new DriveGrant
            {
                DriveId = g.PermissionedDrive.Drive.Alias,
                PermissionedDrive = g.PermissionedDrive, // keep for now
                KeyStoreKeyEncryptedStorageKey = g.KeyStoreKeyEncryptedStorageKey
            });
            
            storageData.KeyStoreKeyEncryptedDriveGrants= updatedAppGrantDriveIdList.ToList();
            appGrant.data = OdinSystemSerializer.Serialize(storageData).ToUtf8ByteArray();

            await db.AppGrantsCached.UpsertAsync(appGrant);
        }

        tx.Commit();
    }
    
    
    /// <summary>
    /// Removes a drive from every circle definition, circle-member grant (identity or YouAuth domain) and app grant.
    /// These rows are where connections hold their circle and app grants, so this covers them too.
    /// </summary>
    public async Task RemoveDriveFromAllGrantsAsync(Guid driveId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await using var tx = await db.BeginStackedTransactionAsync();

        foreach (var circle in await circleDefinitionService.GetCirclesAsync())
        {
            var kept = circle.DriveGrants?.Where(g => g.PermissionedDrive.Drive.Alias != driveId).ToList();
            if (kept != null && kept.Count != circle.DriveGrants!.Count())
            {
                // Unvalidated: a circle that granted only this drive is left granting nothing, which a definition
                // may not be created as, but which keeps its members until the owner edits or deletes it.
                circle.DriveGrants = kept;
                await circleDefinitionService.UpdateAsync(circle, skipValidation: true);
            }
        }

        foreach (var member in await db.CircleMemberCached.GetAllCirclesAsync())
        {
            var storageData = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(member.data.ToStringFromUtf8Bytes());
            if (storageData.CircleGrant?.KeyStoreKeyEncryptedDriveGrants?.RemoveAll(g => g.DriveId == driveId) > 0)
            {
                member.data = OdinSystemSerializer.Serialize(storageData).ToUtf8ByteArray();
                await db.CircleMemberCached.UpsertAsync(member);
            }
        }

        foreach (var appGrant in await db.AppGrantsCached.GetAllAsync())
        {
            var grant = OdinSystemSerializer.Deserialize<AppCircleGrant>(appGrant.data.ToStringFromUtf8Bytes());
            if (grant.KeyStoreKeyEncryptedDriveGrants?.RemoveAll(g => g.DriveId == driveId) > 0)
            {
                appGrant.data = OdinSystemSerializer.Serialize(grant).ToUtf8ByteArray();
                await db.AppGrantsCached.UpsertAsync(appGrant);
            }
        }

        tx.Commit();
    }

    public async Task DeleteMemberFromAllCirclesAsync(AsciiDomainName domainName, DomainType domainType)
    {
        //Note: I updated this to delete by a given domain type so when you login via youauth, your ICR circles are not deleted -_-
        var memberId = OdinId.ToHashId(domainName);

        await using var tx = await db.BeginStackedTransactionAsync();
        var circleMemberRecords = await db.CircleMemberCached.GetMemberCirclesAndDataAsync(memberId);

        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data
                .ToStringFromUtf8Bytes());
            if (sd.DomainType == domainType)
            {
                await db.CircleMemberCached.DeleteAsync(sd.CircleGrant.CircleId, memberId);
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// Deletes every membership row of a circle, whoever holds it.  For a circle being deleted outright,
    /// once the records that are the source of truth no longer name it.
    /// </summary>
    public async Task DeleteAllMembersOfCircleAsync(Guid circleId)
    {
        var rows = await db.CircleMemberCached.GetCircleMembersAsync(circleId);
        if (rows.Count > 0)
        {
            await db.CircleMemberCached.RemoveCircleMembersAsync(circleId, rows.Select(r => r.memberId).ToList());
        }
    }

    public async Task<IEnumerable<CircleGrant>> GetCirclesGrantsByDomainAsync(AsciiDomainName domainName, DomainType domainType)
    {
        var records =
            await db.CircleMemberCached.GetMemberCirclesAndDataAsync(OdinId.ToHashId(domainName));
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

        }

        var memberBytesList = await db.CircleMemberCached.GetCircleMembersAsync(circleId);
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

        // db.CircleMember.Insert(circleMemberRecord);
        await db.CircleMemberCached.UpsertAsync(circleMemberRecord);
        // db.CircleMember.UpsertCircleMembers([circleMemberRecord]);
    }

    // Grants

    public async Task<CircleGrant> CreateCircleGrantAsync(SensitiveByteArray keyStoreKey, CircleDefinition def,
        IStorageKeySource storageKeySource,
        IOdinContext odinContext)
    {
        if (null == def)
        {
            throw new OdinSystemException("Invalid circle definition");
        }

        //map the exchange grant to a structure that matches ICR
        //(the KeyStore wrapper is discarded, so no master key wrap is needed here)
        var grant = await exchangeGrantService.CreateExchangeGrantAsync(keyStoreKey, def.Permissions, def.DriveGrants,
            storageKeySource, masterKey: null, icrKey: null);
        return new CircleGrant()
        {
            CircleId = def.Id,
            KeyStoreKeyEncryptedDriveGrants = grant.DriveGrants,
            PermissionSet = grant.PermissionSet
        };
    }

    public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantListAsync(
        SensitiveByteArray keyStoreKey,
        List<GuidId> circleIds,
        IStorageKeySource storageKeySource,
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

            var cg = await this.CreateCircleGrantAsync(keyStoreKey, def, storageKeySource, null);


            if (!circleGrants.TryAdd(id.Value, cg))
            {
                logger.LogError("CreateCircleGrantList attempted to insert duplicate key [{keyValue}]", id.Value);
            }
        }

        return circleGrants;
    }

    public async Task<(Dictionary<Guid, KeyStore> exchangeGrants, List<GuidId> enabledCircles)> MapCircleGrantsToExchangeGrantsAsync(
        AsciiDomainName domainName,
        List<CircleGrant> circleGrants,
        IOdinContext odinContext)
    {
        //TODO: this code needs to be refactored to avoid all the mapping

        // Map CircleGrants to Exchange Grants
        // Note: remember that all connected users are added to a system
        // circle; this circle has grants to all drives marked allowAnonymous == true

        var grants = new Dictionary<Guid, KeyStore>();
        var enabledCircles = new List<GuidId>();
        foreach (var cg in circleGrants)
        {
            var (enabled, exists) = await CircleIsEnabledAsync(cg.CircleId);             
            if (enabled)
            {
                enabledCircles.Add(cg.CircleId);
                grants.Add(cg.CircleId, new KeyStore()
                {
                    Created = 0,
                    Modified = 0,
                    IsRevoked = false, //TODO

                    DriveGrants = cg.KeyStoreKeyEncryptedDriveGrants,
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

        await mediator.Publish(new CircleDefinitionChangedNotification
        {
            OdinContext = odinContext,
            CircleId = request.Id,
            Change = CircleDefinitionChangeType.Created,
        });
    }

    /// <summary>
    /// Creates a circle owned by the calling app, and returns the id the server picked for it.
    /// </summary>
    /// <remarks>
    /// No permission key: an app is the owner acting, and the circle is its own.  What it may put on the circle is
    /// bounded by what it already has, so creating one is never a way to hand out more -- drive access only as far as
    /// it holds it itself, and on a drive it does not own only Read with the storage key
    /// (<see cref="CircleDefinitionService.AssertAppMayGrantDrivesAsync"/>); permission keys only ones it holds -- and
    /// the circle is granted only explicitly
    /// (<see cref="CircleGrantOn.None"/>), never to every connection on its own.  The id is the server's, so a
    /// deleted circle's id, which leftover deposits and enrollments may still name, is never reused.
    /// </remarks>
    public async Task<Guid> CreateAppCircleAsync(CreateAppCircleRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();

        // The owner console's circles are the owner's; its token must not create them as an app.
        var appId = odinContext.Caller.OdinClientContext?.AppId?.Value;
        if (appId == null || SystemAppConstants.IsOwnerConsole(appId))
        {
            throw new OdinSecurityException("Only an app can create a circle it owns");
        }

        var notHeld = (request.Permissions?.Keys ?? [])
            .Where(key => !odinContext.PermissionsContext.HasPermission(key))
            .ToList();
        if (notHeld.Count > 0)
        {
            throw new OdinSecurityException(
                $"App {appId} cannot grant permission keys it does not hold: {string.Join(", ", notHeld)}");
        }

        await circleDefinitionService.AssertAppMayGrantDrivesAsync(request.DriveGrants, appId.Value, odinContext);

        var circleId = Guid.NewGuid();
        await circleDefinitionService.CreateAsync(new CreateCircleRequest
        {
            Id = circleId,
            Name = request.Name,
            Description = request.Description,
            Emoji = request.Emoji,
            DriveGrants = request.DriveGrants,
            Permissions = request.Permissions,
            AppId = appId,
            GrantOn = CircleGrantOn.None,
            Designation = CircleDesignation.Personal
        });

        await mediator.Publish(new CircleDefinitionChangedNotification
        {
            OdinContext = odinContext,
            CircleId = circleId,
            Change = CircleDefinitionChangeType.Created,
        });

        return circleId;
    }

    /// <summary>
    /// Gets a list of all circle definitions
    /// </summary>
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
        var circles = await circleDefinitionService.GetCirclesAsync();

        // Null AppId means the owner's own circle. An app cannot enrol anyone into one
        // (CircleNetworkService.EnrollInCircleInternalAsync), so offering it would only be a choice that
        // fails; the owner console is scoped to no app and keeps seeing everything.  Behind a tenant flag
        // (off by default) because it also changes what every existing app screen that lists circles sees.
        if ((tenantContext.Settings?.HideOwnerCirclesFromApps ?? false) &&
            odinContext.Caller.OdinClientContext?.AppId != null)
        {
            circles = circles.Where(c => !SystemAppConstants.IsOwnerConsole(c.AppId)).ToList();
        }

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

    /// <summary>
    /// Deletes a circle even though it has members: every member row goes -- identity and YouAuth domain --
    /// and then the definition. Grants a member holds through the circle on its connection record are the
    /// caller's to remove first (<c>CircleNetworkService.RemoveAppFromAllConnectionsAsync</c>).
    /// </summary>
    public async Task DeleteCircleAndMembersAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        await using var tx = await db.BeginStackedTransactionAsync();
        await DeleteAllMembersOfCircleAsync(circleId);
        await circleDefinitionService.DeleteAsync(circleId);
        tx.Commit();
    }

    /// <summary>
    /// Deletes a circle that has no members.
    /// </summary>
    /// <remarks>
    /// The owner console may delete any circle but a system or tree-declared one; an app only one it
    /// owns.  See <see cref="AssertCallerMayManageCircleAsync"/> and
    /// <see cref="CircleDefinitionService.DeleteAsync"/>.
    /// </remarks>
    public async Task DeleteAsync(GuidId circleId, IOdinContext odinContext)
    {
        await AssertCallerMayManageCircleAsync(circleId, "delete", odinContext);
        await circleDefinitionService.DeleteAsync(circleId);
    }

    /// <summary>
    /// Throws unless this caller may delete this circle once it has no members -- the question to ask
    /// before stripping its members, which <see cref="DeleteAsync"/> only answers after.
    /// </summary>
    public async Task AssertCallerMayDeleteAsync(GuidId circleId, IOdinContext odinContext)
    {
        await AssertCallerMayManageCircleAsync(circleId, "delete", odinContext);
        await circleDefinitionService.AssertDeletableAsync(circleId);
    }

    /// <summary>
    /// Drops these domains' membership rows for one circle.  For a YouAuth domain the row is the grant
    /// itself -- <c>YouAuthDomainRegistrationService.SaveRegistrationAsync</c> moves grants into the
    /// rows and keeps no copy -- so this is the whole of revoking it.
    /// </summary>
    public async Task RemoveCircleMembersAsync(GuidId circleId, IEnumerable<AsciiDomainName> domainNames)
    {
        var memberIds = domainNames.Select(d => OdinId.ToHashId(d)).ToList();
        if (memberIds.Count > 0)
        {
            await db.CircleMemberCached.RemoveCircleMembersAsync(circleId, memberIds);
        }
    }

    /// <summary>
    /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
    /// </summary>
    /// <remarks>
    /// The owner console may disable any circle; an app only one it owns.  See
    /// <see cref="AssertCallerMayManageCircleAsync"/>.
    /// </remarks>
    public Task DisableCircleAsync(GuidId circleId, IOdinContext odinContext) =>
        SetDisabledAsync(circleId, true, odinContext);

    /// <summary>
    /// Enables a circle
    /// </summary>
    public Task EnableCircleAsync(GuidId circleId, IOdinContext odinContext) =>
        SetDisabledAsync(circleId, false, odinContext);

    private async Task SetDisabledAsync(GuidId circleId, bool disabled, IOdinContext odinContext)
    {
        await AssertCallerMayManageCircleAsync(circleId, disabled ? "disable" : "enable", odinContext);
        await circleDefinitionService.SetDisabledAsync(circleId, disabled);

        await mediator.Publish(new CircleDefinitionChangedNotification
        {
            OdinContext = odinContext,
            CircleId = circleId.Value,
            Change = disabled ? CircleDefinitionChangeType.Disabled : CircleDefinitionChangeType.Enabled,
        });
    }

    /// <summary>
    /// Who may enable, disable or delete a circle: the owner console (master key) any circle; otherwise the
    /// owner acting through an app, and only on a circle that app owns.
    /// </summary>
    /// <remarks>
    /// No permission key: an app is the owner acting, and owning the circle is the whole of its
    /// authority over it.  An owner-console circle (no AppId, or the owner console's) is never an
    /// app's.
    /// </remarks>
    private async Task AssertCallerMayManageCircleAsync(GuidId circleId, string action, IOdinContext odinContext)
    {
        if (odinContext.Caller.HasMasterKey)
        {
            return;
        }

        odinContext.Caller.AssertCallerIsOwner();

        var callerAppId = odinContext.Caller.OdinClientContext?.AppId?.Value;
        if (callerAppId == null)
        {
            throw new OdinSecurityException($"Caller cannot {action} circle {circleId}; it is not an app");
        }

        var circle = await circleDefinitionService.GetCircleAsync(circleId);
        if (null == circle)
        {
            throw new OdinClientException($"Circle {circleId} does not exist", OdinClientErrorCode.CircleNotFound);
        }

        // Both tests are about the circle; the caller is already known to be an app (no master key).
        // A circle the owner owns is never an app's.  That is not implied by the AppId comparison:
        // owner-owned circles are tagged SystemAppId, and System is a registered app a client token can
        // be minted for, so its token would match every owner-owned circle.
        if (SystemAppConstants.IsOwnerConsole(circle.AppId) || circle.AppId != callerAppId)
        {
            throw new OdinSecurityException(
                $"App {callerAppId} cannot {action} circle {circleId}; it belongs to {circle.AppId?.ToString() ?? "the owner"}");
        }
    }

    private async Task<(bool enabled, bool exists)> CircleIsEnabledAsync(GuidId circleId)
    {
        var circle = await circleDefinitionService.GetCircleAsync(circleId);
        var exists = circle != null;
        var enabled = exists && !circle.Disabled;
        return (enabled, exists);
    }
}