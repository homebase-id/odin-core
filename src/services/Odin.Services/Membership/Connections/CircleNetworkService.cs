using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Permissions_PermissionSet = Odin.Services.Authorization.Permissions.PermissionSet;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkService(
        ILogger<CircleNetworkService> logger,
        INodeLock nodeLock,
        ExchangeGrantService exchangeGrantService,
        TenantContext tenantContext,
        IAppRegistrationService appRegistrationService,
        CircleMembershipService circleMembershipService,
        IMediator mediator,
        CircleDefinitionService circleDefinitionService,
        IDriveManager driveManager,
        PublicPrivateKeyService publicPrivateKeyService,
        CircleNetworkStorage circleNetworkStorage,
        PeerOutbox peerOutbox,
        OdinContextCache odinContextCache,
        IdentityDatabase db)
        : INotificationHandler<DriveDefinitionAddedNotification>,
            INotificationHandler<AppRegistrationChangedNotification>
    {
        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        public async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContextAsync(
            OdinId odinId,
            ClientAuthenticationToken remoteIcrToken,
            IOdinContext odinContext)
        {
            logger.LogDebug("Creating transit permission context for [{odinId}]", odinId);

            var icr = await this.GetIcrAsync(odinId, remoteIcrToken);

            if (!icr.PeerKeyStore?.IsValid() ?? false)
            {
                logger.LogDebug("Creating transit permission context for [{odinId}] - Failed due to invalid access grant", odinId);
                throw new OdinSecurityException("Invalid token")
                {
                    IsRemoteIcrIssue = true
                };
            }

            if (!icr.IsConnected())
            {
                logger.LogDebug("Creating transit permission context for [{odinId}] - Failed due to invalid connection", odinId);
                throw new OdinSecurityException("Invalid connection")
                {
                    IsRemoteIcrIssue = true
                };
            }

            // the peer's token has the key store key in scope - convert any pending
            // deposited grants so they apply to this very request
            if (await TryConvertDepositedGrantsAtPeerAuthAsync(icr, remoteIcrToken, odinContext))
            {
                // saving clears the in-memory grant collections (they live in sibling tables);
                // re-fetch so this request's permission context sees the converted grants
                icr = await this.GetIcrAsync(odinId, remoteIcrToken);
            }

            var (permissionContext, enabledCircles) = await CreatePermissionContextInternalAsync(
                icr: icr,
                authToken: remoteIcrToken,
                accessReg: icr.PeerKeyStore!.PeerClientKey,
                applyAppCircleGrants: true,
                odinContext);

            return (permissionContext, enabledCircles);
        }

        /// <summary>
        /// Tries to create caller and permission context for the given OdinId if is connected
        /// </summary>
        public async Task<IOdinContext> TryCreateConnectedYouAuthContextAsync(OdinId odinId, ClientAuthenticationToken authToken,
            ServerHalfOfClientKey accessReg,
            IOdinContext odinContext)
        {
            logger.LogDebug("TryCreateConnectedYouAuthContext for {id}", odinId);

            var icr = await GetIdentityConnectionRegistrationInternalAsync(odinId);
            bool isValid = icr.PeerKeyStore?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            if (icr.Status == ConnectionStatus.Blocked)
            {
                return null;
            }

            // Only return the permissions if the identity is connected.
            if (isValid && isConnected)
            {
                var (permissionContext, enabledCircles) = await CreatePermissionContextInternalAsync(
                    icr: icr,
                    authToken: authToken,
                    accessReg: accessReg,
                    applyAppCircleGrants: false,
                    odinContext: odinContext);


                var transientTempDrive = SystemDriveConstants.TransientTempDrive;
                var transientTempDriveGrant = new DriveGrant()
                {
                    DriveId = transientTempDrive.Alias,
                    PermissionedDrive = new()
                    {
                        Drive = transientTempDrive,
                        Permission = DrivePermission.Write
                    },
                    KeyStoreKeyEncryptedStorageKey = null
                };

                permissionContext.PermissionGroups.Add(
                    "grant_transient_temp_drive_to_connected_youauth_identity",
                    new PermissionGroup(
                        new PermissionSet(new[] { PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections }),
                        new List<DriveGrant>() { transientTempDriveGrant }, null, null));


                var context = new OdinContext()
                {
                    Caller = new CallerContext(
                        odinId: odinId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Connected,
                        circleIds: enabledCircles)
                };

                context.SetPermissionContext(permissionContext);
                return context;
            }

            return null;
        }

        /// <summary>
        /// Disconnects you from the specified <see cref="OdinId"/>.
        /// </summary>
        /// <param name="notifyRemote">
        /// When true (the default), the remote identity is notified (best-effort, via the outbox) so
        /// it severs its side of the connection as well. When false, the disconnect is one-sided and
        /// the remote identity keeps its record until it independently reconciles the asymmetry.
        /// </param>
        public async Task<bool> DisconnectAsync(OdinId odinId, IOdinContext odinContext, bool notifyRemote = true)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);
            return await DisconnectInternalAsync(odinId, notifyRemote, odinContext);
        }

        /// <summary>
        /// Handles an inbound notification that the caller has severed their connection with us; we
        /// sever ours in return.  Invoked from the peer perimeter, where the caller's identity has
        /// already been verified, so this does not require the owner's <see cref="PermissionKeys.ManageContacts"/>.
        /// </summary>
        public async Task ReceiveRemoteDisconnectAsync(IOdinContext odinContext)
        {
            // Guard against a stale notification tearing down a re-established connection.
            // The caller's identity is proven by certificate, but their *connection* is proven by an
            // access token that matches our current AccessRegistration. If the connection was severed
            // and later re-established, a break-connection still pending in the caller's outbox carries
            // the old token; that no longer validates, so the perimeter downgrades the caller below
            // Connected. Requiring a connected caller here ensures we only honor a disconnect for the
            // connection instance the caller actually still shares with us.
            odinContext.Caller.AssertCallerIsConnected();
            var caller = odinContext.GetCallerOdinIdOrFail();

            // notifyRemote:false -- the caller initiated this; echoing the notification back would loop.
            await DisconnectInternalAsync(caller, notifyRemote: false, odinContext);
        }

        private async Task<bool> DisconnectInternalAsync(OdinId odinId, bool notifyRemote, IOdinContext odinContext)
        {
            // overrideHack/no-upgrade: the inbound-peer path runs under a context without ReadConnections,
            // and we're about to delete the record anyway, so skip the permission check and token upgrade.
            var info = await this.GetIcrAsync(odinId, odinContext, overrideHack: true, tryUpgradeEncryption: false);
            if (info is { Status: ConnectionStatus.Connected })
            {
                // Capture the access token to the remote identity BEFORE deleting the connection record,
                // so the outbox worker can still authenticate to them once the local record is gone.
                ClientAccessToken remoteToken = null;
                if (notifyRemote)
                {
                    try
                    {
                        remoteToken = info.EncryptedClientAccessToken?.Decrypt(odinContext.PermissionsContext.GetIcrKey());
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "Could not resolve access token for [{odinId}] while disconnecting; " +
                                           "the remote identity will not be notified to disconnect in return.", odinId);
                    }
                }

                await circleNetworkStorage.DeleteAsync(odinId);

                if (notifyRemote && remoteToken != null)
                {
                    await EnqueueBreakConnectionNotificationAsync(odinId, remoteToken);
                }

                await mediator.Publish(new ConnectionDeletedNotification()
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                });

                await mediator.Publish(new ConnectionChangedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    Change = ConnectionChangeType.Disconnected,
                });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Queues a best-effort, retried notification telling the remote identity that we have
        /// disconnected, so they disconnect from us in return.
        /// </summary>
        private async Task EnqueueBreakConnectionNotificationAsync(OdinId recipient, ClientAccessToken remoteToken)
        {
            var item = new OutboxFileItem
            {
                Recipient = recipient,
                Priority = 50, //super high priority to ensure these are sent quickly
                Type = OutboxItemType.BreakConnectionRequest,
                AttemptCount = 0,
                File = new InternalDriveFileId()
                {
                    DriveId = SystemDriveConstants.TransientTempDrive.Alias,
                    FileId = recipient.ToHashId()
                },
                DependencyFileId = default,
                State = new OutboxItemState
                {
                    TransferInstructionSet = null,
                    OriginalTransitOptions = null,
                    EncryptedClientAuthToken = remoteToken.ToPortableBytes(),
                    Data = Array.Empty<byte>()
                },
            };

            await peerOutbox.AddItemAsync(item, useUpsert: true);
        }

        /// <summary>
        /// Blocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> BlockAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

            var info = await this.GetIcrAsync(odinId, odinContext);

            // NOTE: blocking intentionally retains the AccessGrant rather than destroying it.
            // A blocked identity is already denied everywhere because every auth path gates on
            // connection status, not merely on token/grant validity: peer/transit via
            // CreateTransitPermissionContextAsync's IsConnected() check, and guest/YouAuth via
            // TryCreateConnectedYouAuthContextAsync (explicit Blocked short-circuit) and
            // HomeAuthenticatorService's isConnected gate. Keeping the grant intact is what lets
            // UnblockAsync restore the prior connection without a fresh connection request; revoking
            // it here would force unblock to always fall through to ConnectionStatus.None.

            if (info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                info.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await this.SaveIcrAsync(info, odinContext);

                await mediator.Publish(new ConnectionBlockedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                });

                await mediator.Publish(new ConnectionChangedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    Change = ConnectionChangeType.Blocked,
                });

                return true;
            }

            if (info.Status == ConnectionStatus.Blocked || info.Status == ConnectionStatus.None)
            {
                info.Status = ConnectionStatus.Blocked;
                info.Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                info.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await this.SaveIcrAsync(info, odinContext);

                await mediator.Publish(new ConnectionBlockedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                });

                await mediator.Publish(new ConnectionChangedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    Change = ConnectionChangeType.Blocked,
                });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        public async Task<CursoredResult<IdentityConnectionRegistration>> GetBlockedProfilesAsync(int count, string cursor,
            IOdinContext odinContext)
        {
            return await GetConnectionsInternalAsync(count, cursor, ConnectionStatus.Blocked, odinContext);
        }

        /// <summary>
        /// Returns a list of identities which are connected to this DI
        /// </summary>
        public async Task<CursoredResult<IdentityConnectionRegistration>> GetConnectedIdentitiesAsync(int count, string cursor,
            IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            return await GetConnectionsInternalAsync(count, cursor, ConnectionStatus.Connected, odinContext);
        }

        /// <summary>
        /// Unblocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> UnblockAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

            var info = await this.GetIcrAsync(odinId, odinContext);
            if (info.Status == ConnectionStatus.Blocked)
            {
                bool isValid = info.PeerKeyStore?.IsValid() ?? false;

                info.Status = isValid ? ConnectionStatus.Connected : ConnectionStatus.None;
                await this.SaveIcrAsync(info, odinContext);

                await mediator.Publish(new ConnectionChangedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    Change = ConnectionChangeType.Unblocked,
                });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIcrAsync(OdinId odinId, IOdinContext odinContext,
            bool overrideHack = false,
            bool tryUpgradeEncryption = true)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS
            if (!overrideHack)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            var icr = await GetIdentityConnectionRegistrationInternalAsync(odinId);

            // 
            if (tryUpgradeEncryption)
            {
                await this.UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);
                icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
            }

            return icr;
        }

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">x-token half key</param> is valid
        /// </summary>
        public async Task<IdentityConnectionRegistration> GetIcrAsync(
            OdinId odinId,
            ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (connection?.PeerKeyStore?.PeerClientKey == null)
            {
                throw new OdinSecurityException("Unauthorized Action") { IsRemoteIcrIssue = true };
            }

            connection.PeerKeyStore.PeerClientKey.AssertValidRemoteKey(remoteClientAuthenticationToken.AccessTokenHalfKey);

            return connection;
        }


        /// <summary>
        /// Determines if the specified odinId is connected
        /// </summary>
        public async Task<bool> IsConnectedAsync(OdinId odinId, IOdinContext odinContext)
        {
            //allow the caller to see if s/he is connected, otherwise
            if (odinContext.Caller.OdinId != odinId)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            var info = await this.GetIcrAsync(odinId, odinContext);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task<IEnumerable<OdinId>> GetCircleMembersAsync(GuidId circleId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
            //added override:true because PermissionKeys.ReadCircleMembership is present
            var result = (await circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext, overrideHack: true))
                .Where(d => d.DomainType == DomainType.Identity)
                .Select(m => new OdinId(m.Domain));
            return result;
        }

        /// <summary>
        /// Fills in the circle and app names on each connection's awaiting-app entries, so a client can
        /// say which app is being waited on rather than only that something is.
        /// </summary>
        /// <remarks>
        /// Resolved on read rather than stored at enqueue time, so a circle or app renamed since the
        /// review reads as it is called now.  Lookups are memoised across the whole batch, so a list of
        /// connections all waiting on the same app costs one lookup, not one per connection -- and a
        /// connection with nothing awaiting costs none at all, which is nearly all of them.
        /// <para>
        /// A name that cannot be resolved is left null rather than failing the read: a circle or app
        /// deleted while an entry waited is a real state, and the entry still needs to be reportable so
        /// the owner can see something is stuck.
        /// </para>
        /// </remarks>
        public async Task PopulateAwaitingAppNamesAsync(
            IEnumerable<RedactedIdentityConnectionRegistration> connections,
            IOdinContext odinContext)
        {
            var circles = new Dictionary<Guid, CircleDefinition>();
            var appNames = new Dictionary<Guid, string>();

            foreach (var connection in connections ?? [])
            {
                foreach (var awaiting in connection?.AccessGrant?.AwaitingApps ?? [])
                {
                    if (!circles.TryGetValue(awaiting.CircleId, out var circle))
                    {
                        circle = await circleDefinitionService.GetCircleAsync(awaiting.CircleId);
                        circles[awaiting.CircleId] = circle;
                    }

                    awaiting.CircleName = circle?.Name;

                    // Who it waits on comes from the circle, not from the entry's copy. The copy is
                    // taken when the entry is queued, so a circle adopted or moved since then leaves it
                    // naming the previous owner -- and this is the value the connection screen renders
                    // as "waiting on X". Overwritten rather than trusted, for the same reason
                    // ProcessPendingEnrollmentsForAppAsync stopped filtering on it: the definition is
                    // loaded here anyway.
                    awaiting.AppId = circle?.AppId;

                    if (!awaiting.AppId.HasValue)
                    {
                        continue;
                    }

                    if (!appNames.TryGetValue(awaiting.AppId.Value, out var appName))
                    {
                        appName = (await appRegistrationService.GetAppRegistration(awaiting.AppId.Value, odinContext))?.Name;
                        appNames[awaiting.AppId.Value] = appName;
                    }

                    awaiting.AppName = appName;
                }
            }
        }

        /// <summary>
        /// Identities whose grant for <paramref name="circleId"/> is deposited but not yet converted --
        /// asked for, not yet in effect.
        /// </summary>
        /// <remarks>
        /// Reported beside <see cref="GetCircleMembersAsync"/> rather than folded into it: a pending
        /// identity is not a member, and saying otherwise would claim access that does not exist yet.
        /// <para>
        /// This scans connections, where the member list is one indexed read.  Deposits live inside each
        /// connection's own key store and nothing indexes circle to deposit, so there is no cheaper
        /// answer; the set is small and shrinks on its own as deposits convert.  Prefer
        /// <see cref="GetAllPendingCircleMembersAsync"/> when asking about more than one circle -- it
        /// makes the same single pass and groups the answer.
        /// </para>
        /// </remarks>
        public async Task<List<PendingCircleMember>> GetPendingCircleMembersAsync(GuidId circleId, IOdinContext odinContext)
        {
            var all = await GetAllPendingCircleMembersAsync(odinContext);
            return all.TryGetValue(circleId.Value, out var members) ? members : [];
        }

        /// <summary>
        /// Every pending circle membership across all connections, keyed by circle id.  One pass, so a
        /// caller listing many circles does not scan once per circle.
        /// </summary>
        public async Task<Dictionary<Guid, List<PendingCircleMember>>> GetAllPendingCircleMembersAsync(IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);

            var pending = new Dictionary<Guid, List<PendingCircleMember>>();

            string cursor = null;
            do
            {
                var page = await GetConnectionsInternalAsync(int.MaxValue, cursor, ConnectionStatus.Connected, odinContext);
                cursor = page.Cursor;

                foreach (var icr in page.Results)
                {
                    foreach (var deposit in icr.PeerKeyStore?.DepositedGrants ?? [])
                    {
                        if (!pending.TryGetValue(deposit.CircleId.Value, out var members))
                        {
                            members = [];
                            pending[deposit.CircleId.Value] = members;
                        }

                        members.Add(new PendingCircleMember
                        {
                            OdinId = icr.OdinId,
                            Deposited = deposit.Deposited,
                            DepositingAppId = deposit.DepositingAppId
                        });
                    }
                }
            } while (!string.IsNullOrEmpty(cursor));

            return pending;
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="odinContext"></param>
        /// <returns></returns>
        public async Task AssertConnectionIsNoneOrValidAsync(OdinId odinId, IOdinContext odinContext)
        {
            var info = await this.GetIcrAsync(odinId, odinContext);
            this.AssertConnectionIsNoneOrValid(info);
        }

        /// <summary>
        /// Adds the specified odinId to your network
        /// </summary>
        /// <returns></returns>
        public async Task ConnectAsync(string odinIdentity, PeerKeyStore accessGrant,
            (EncryptedClientAccessToken EncryptedCat, (EccEncryptedPayload Token, EccEncryptedPayload KeyStoreKey) Temp) keys,
            ContactRequestData contactData,
            ConnectionRequestOrigin connectionRequestOrigin,
            OdinId? introducerOdinId,
            byte[] verificationHash,
            IOdinContext odinContext)
        {
            //TODO: need to add security that this method can be called

            var odinId = (OdinId)odinIdentity;

            //TODO: need to scan the YouAuthServiceClassic to see if this user has a HomeAppIdentityRegistration

            // Add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                OdinId = odinId,
                Status = ConnectionStatus.Connected,
                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                OriginalContactData = contactData,
                PeerKeyStore = accessGrant,
                EncryptedClientAccessToken = keys.EncryptedCat, //may come in as NULL; meaning this cannot be used until we have the ICR key
                TemporaryWeakClientAccessToken = keys.Temp.Token,
                TempWeakKeyStoreKey = keys.Temp.KeyStoreKey,
                ConnectionRequestOrigin = connectionRequestOrigin,
                IntroducerOdinId = introducerOdinId,
                VerificationHash = verificationHash
            };

            await this.SaveIcrAsync(newConnection, odinContext);

            await mediator.Publish(new ConnectionFinalizedNotification()
            {
                OdinId = odinId,
                OdinContext = odinContext,
            });
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the odinId
        /// </summary>
        /// <summary>
        /// Gives access to all resource granted by the specified circle to the odinId
        /// </summary>
        /// <remarks>
        /// Deliberately does its own minting and depositing rather than going through
        /// <see cref="EnrollInCircleInternalAsync"/>, which the review path uses.  This is the older,
        /// app-callable way in, and its behaviour is held exactly as it was: a write-only circle deposits
        /// here rather than minting outright, and a circle belonging to no app is allowed.  Both of those
        /// differ from enrolment, and changing them under an existing caller is not worth the shared code.
        /// </remarks>
        public async Task GrantCircleAsync(GuidId circleId, OdinId odinId, IOdinContext odinContext)
        {
            AssertCanManageCircleMembership(odinContext);

            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinSecurityException($"{odinId} must have valid connection to be added to a circle");
            }

            if (icr.PeerKeyStore.CircleGrants.TryGetValue(SystemCircleConstants.AutoConnectionsCircleId, out _))
            {
                throw new OdinClientException(
                    $"Cannot grant additional circles to auto-connected identity.  You must first confirm the connection.",
                    OdinClientErrorCode.CannotGrantAutoConnectedMoreCircles);
            }

            if (icr.PeerKeyStore.CircleGrants.TryGetValue(circleId, out _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{odinId} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = await circleMembershipService.GetCircleAsync(circleId, odinContext);

            if (odinContext.Caller.HasMasterKey)
            {
                // The store may have been created without the owner online (e.g. an app accepted the
                // connection request), in which case the Peer Key is only held as the temp weak key.
                // Re-mint it under the master key before we try to decrypt it.
                if (await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext))
                {
                    // refetch the record since the above method just writes to db
                    icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                }

                if (icr.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                {
                    throw new OdinSystemException(
                        $"Cannot grant circle to {odinId}; the peer key store still requires master key encryption upgrade");
                }

                var masterKey = odinContext.Caller.GetMasterKey();
                var keyStoreKey = icr.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
                var storageKeySource = new MasterKeyStorageKeySource(masterKey);

                // The owner touch provisions the write-only keypair on stores created before it
                // existed and converts anything apps have deposited in the meantime.
                icr.PeerKeyStore.WriteOnlyKeyPair ??= PeerKeyStoreWriteOnlyKey.CreateKeyPair(keyStoreKey);
                var convertedCircleIds = await ConvertDepositedGrantsAsync(icr.PeerKeyStore, keyStoreKey, odinContext);

                if (!icr.PeerKeyStore.CircleGrants.ContainsKey(circleId))
                {
                    var circleGrant =
                        await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDefinition, storageKeySource, odinContext);
                    icr.PeerKeyStore.CircleGrants.Add(circleGrant.CircleId, circleGrant);
                }

                // Check the apps.  If a circle being granted is authorized by an app
                // ensure the new member gets the permissions given by the app
                await FanOutAppCircleGrantsAsync(icr, keyStoreKey, convertedCircleIds.Append(circleId.Value).Distinct(),
                    storageKeySource, odinContext);

                keyStoreKey.Wipe();
            }
            else
            {
                // Blocker #3: the caller (an app) has no path to this connection's key store key
                // and must not be handed one. It deposits the grant via the store's write-only
                // public key instead; the deposit converts to a normal circle grant the next time
                // the key store key is in scope (peer CAT auth or the owner's next grant touch).
                if (icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleId))
                {
                    throw new OdinClientException($"{odinId} is already member of circle",
                        OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
                }

                var deposit = await CreateDepositedGrantAsync(icr.PeerKeyStore, circleDefinition, odinContext);
                icr.PeerKeyStore.DepositedGrants.Add(deposit);
            }

            await this.SaveIcrAsync(icr, odinContext);

            await mediator.Publish(new ConnectionChangedNotification
            {
                OdinContext = odinContext,
                OdinId = odinId,
                CircleId = circleId.Value,
                Change = ConnectionChangeType.CircleGranted,
            });
        }

        /// <summary>
        /// Enrolls <paramref name="odinId"/> in a circle, minting the grant or depositing it when the
        /// caller holds no master key.  Unlike <see cref="GrantCircleAsync"/> this is idempotent -- an
        /// existing membership is a no-op rather than an error -- and it does not refuse auto-connected
        /// identities.
        /// </summary>
        /// <remarks>
        /// Those two differences are exactly what the connection review needs: it enrolls the circles the
        /// owner checked in one act, over a contact who is very likely auto-connected and may already hold
        /// some of them (docs/connection-defaults.md, "On verify": "Enrollment is idempotent -- already a
        /// member is a no-op").  This method performs no permission check; the caller owns that.
        /// </remarks>
        private async Task EnrollInCircleInternalAsync(GuidId circleId, OdinId odinId, IOdinContext odinContext,
            bool enqueueWhenOutOfReach = false)
        {
            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinSecurityException($"{odinId} must have valid connection to be added to a circle");
            }

            if (icr.PeerKeyStore.CircleGrants.ContainsKey(circleId))
            {
                return;
            }

            var circleDefinition = await circleMembershipService.GetCircleAsync(circleId, odinContext);

            // A circle with no owning app is the owner's own, and an app has no business putting anyone
            // into one: nothing an app does should leave the contact waiting on the owner opening their
            // console. Apps are not shown these circles either
            // (CircleMembershipService.GetCircleDefinitions), so a well-behaved client never asks.
            if (!circleDefinition.AppId.HasValue && odinContext.Caller.OdinClientContext?.AppId != null)
            {
                throw new OdinSecurityException(
                    $"An app cannot add {odinId} to circle {circleId}; it belongs to the owner, not to an app");
            }

            // The owner chose a circle this caller cannot grant -- another app's, whose drives it cannot
            // read. Record the intent so the app that can grant it may finish later, rather than failing an
            // act the owner was entitled to perform. Recording confers nothing: whoever processes the entry
            // re-checks scope then.
            if (enqueueWhenOutOfReach && !odinContext.Caller.HasMasterKey &&
                !await CallerCanGrantCircleAsync(circleDefinition, odinContext))
            {
                EnqueuePendingEnrollment(icr, circleDefinition, odinContext);
                await this.SaveIcrAsync(icr, odinContext);
                return;
            }

            if (odinContext.Caller.HasMasterKey)
            {
                // The store may have been created without the owner online (e.g. an app accepted the
                // connection request), in which case the Peer Key is only held as the temp weak key.
                // Re-mint it under the master key before we try to decrypt it.
                if (await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext))
                {
                    // refetch the record since the above method just writes to db
                    icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                }

                if (icr.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                {
                    throw new OdinSystemException(
                        $"Cannot grant circle to {odinId}; the peer key store still requires master key encryption upgrade");
                }

                var masterKey = odinContext.Caller.GetMasterKey();
                var keyStoreKey = icr.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
                var storageKeySource = new MasterKeyStorageKeySource(masterKey);

                // The owner touch provisions the write-only keypair on stores created before it
                // existed and converts anything apps have deposited in the meantime.
                icr.PeerKeyStore.WriteOnlyKeyPair ??= PeerKeyStoreWriteOnlyKey.CreateKeyPair(keyStoreKey);
                var convertedCircleIds = await ConvertDepositedGrantsAsync(icr.PeerKeyStore, keyStoreKey, odinContext);

                if (!icr.PeerKeyStore.CircleGrants.ContainsKey(circleId))
                {
                    var circleGrant =
                        await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDefinition, storageKeySource, odinContext);
                    icr.PeerKeyStore.CircleGrants.Add(circleGrant.CircleId, circleGrant);
                }

                // Check the apps.  If a circle being granted is authorized by an app
                // ensure the new member gets the permissions given by the app
                await FanOutAppCircleGrantsAsync(icr, keyStoreKey, convertedCircleIds.Append(circleId.Value).Distinct(),
                    storageKeySource, odinContext);

                keyStoreKey.Wipe();
            }
            else if (!circleDefinition.RequiresPeerKey())
            {
                // Nothing in this grant is sealed to anything, so the Peer Key the caller cannot reach is
                // not needed: a write/react grant is a plaintext {driveId, permission} record. Depositing
                // it would seal nothing, convert to the same record, and leave the member out of the
                // circle until something else happened to touch the connection.
                //
                // The deposit path's write-side scope check has to come along, though. CreateDriveGrant
                // makes no such check -- for the owner it is redundant -- so without this an app could
                // grant write on a drive it cannot write to itself.
                AssertCallerHoldsGrantedDrivePermissions(circleDefinition, odinContext);

                var circleGrant = await circleMembershipService.CreateCircleGrantAsync(
                    keyStoreKey: null,
                    circleDefinition,
                    new PermissionContextStorageKeySource(odinContext),
                    odinContext);

                icr.PeerKeyStore.CircleGrants.Add(circleGrant.CircleId, circleGrant);

                // Same tradeoff, and the same reasoning, as the peer-CAT conversion path: no master key
                // here, so an app-authorized circle that wants Read comes out keyless and self-heals when
                // ReconcileAuthorizedCircles next runs for that app.
                await FanOutAppCircleGrantsAsync(icr, keyStoreKey: null, [circleId.Value],
                    NoStorageKeySource.Instance, odinContext);
            }
            else
            {
                // Blocker #3: the caller (an app) has no path to this connection's key store key
                // and must not be handed one. It deposits the grant via the store's write-only
                // public key instead; the deposit converts to a normal circle grant the next time
                // the key store key is in scope (peer CAT auth or the owner's next grant touch).
                if (icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleId))
                {
                    return;
                }

                var deposit = await CreateDepositedGrantAsync(icr.PeerKeyStore, circleDefinition, odinContext);
                icr.PeerKeyStore.DepositedGrants.Add(deposit);
            }

            await this.SaveIcrAsync(icr, odinContext);

            await mediator.Publish(new ConnectionChangedNotification
            {
                OdinContext = odinContext,
                OdinId = odinId,
                CircleId = circleId.Value,
                Change = ConnectionChangeType.CircleGranted,
            });
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccessAsync(GuidId circleId, OdinId odinId, IOdinContext odinContext)
        {
            AssertCanManageCircleMembership(odinContext);

            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
            if (icr.PeerKeyStore == null)
            {
                return;
            }

            if (icr.PeerKeyStore.CircleGrants.ContainsKey(circleId))
            {
                if (!icr.PeerKeyStore.CircleGrants.Remove(circleId))
                {
                    throw new OdinClientException($"Failed to remove {circleId} from {odinId}");
                }
            }

            // also purge any not-yet-converted deposit for this circle
            icr.PeerKeyStore.DepositedGrants?.RemoveAll(d => d.CircleId == circleId);

            //find the circle grant across all app grants and remove it
            foreach (var (_, appCircleGrants) in icr.PeerKeyStore.AppGrants)
            {
                appCircleGrants.Remove(circleId.Value);
            }

            await this.SaveIcrAsync(icr, odinContext);

            await mediator.Publish(new ConnectionChangedNotification
            {
                OdinContext = odinContext,
                OdinId = odinId,
                CircleId = circleId.Value,
                Change = ConnectionChangeType.CircleRevoked,
            });
        }

        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantListWithSystemCircle(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circleIds,
            ConnectionRequestOrigin origin,
            IStorageKeySource storageKeySource,
            IOdinContext odinContext)
        {
            var list = CircleNetworkUtils.EnsureSystemCircles(circleIds, origin);
            return await this.CreateAppCircleGrantList(keyStoreKey, list, storageKeySource, odinContext);
        }


        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantList(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circleIds,
            IStorageKeySource storageKeySource,
            IOdinContext odinContext)
        {
            var appGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>();

            foreach (var circleId in circleIds)
            {
                var appsThatGrantThisCircle = await appRegistrationService.GetAppsGrantingCircleAsync(circleId, odinContext);

                foreach (var app in appsThatGrantThisCircle)
                {
                    var appKey = app.AppId.Value;
                    var appCircleGrant = await this.CreateAppCircleGrantAsync(app, keyStoreKey, circleId, storageKeySource);

                    if (!appGrants.TryGetValue(appKey, out var appCircleGrantsDictionary))
                    {
                        appCircleGrantsDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    appCircleGrantsDictionary[circleId.Value] = appCircleGrant;
                    appGrants[appKey] = appCircleGrantsDictionary;
                }
            }

            return appGrants;
        }


        /// <summary>
        /// Updates a <see cref="CircleDefinition"/> and applies permission and drive changes to all existing circle members
        /// </summary>
        public async Task UpdateCircleDefinitionAsync(CircleDefinition circleDef, IOdinContext odinContext)
        {
            await circleMembershipService.AssertValidDriveGrantsAsync(circleDef.DriveGrants);

            var members = await GetCircleMembersAsync(circleDef.Id, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();

            // Per-member, and named: saving an ICR publishes notifications, and on an identity whose
            // peers are unreachable those can block.  Without a line per member a stall here looks like
            // the whole call hanging, when what matters is whether it is stuck on one member or slowly
            // grinding through many.
            logger.LogDebug("UpdateCircleDefinition '{circle}': re-granting {count} member(s)",
                circleDef.Name, members.Count());

            var swCircle = System.Diagnostics.Stopwatch.StartNew();
            var memberIndex = 0;

            // List<OdinId> invalidMembers = new List<OdinId>();
            foreach (var odinId in members)
            {
                memberIndex++;
                logger.LogDebug("UpdateCircleDefinition '{circle}': member {index}/{count} {odinId}",
                    circleDef.Name, memberIndex, members.Count(), odinId);

                var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

                var circleKey = circleDef.Id;
                var hasCg = icr.PeerKeyStore.CircleGrants.Remove(circleKey, out _);

                if (icr.IsConnected() && hasCg)
                {
                    if (icr.PeerKeyStore == null)
                    {
                        logger.LogError("icr for {odinID} has null access grant", odinId);
                    }

                    await UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);

                    if (await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext))
                    {
                        // refetch the record since the above method just writes to db
                        icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                        
                        if (icr.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                        {
                            logger.LogError("After Refetch ICR for {identity} STILL Requires MasterKey Encryption Upgrade; " +
                                            "something is wrong for sure", icr.OdinId);
                        }
                    }

                    if (icr.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                    {
                        logger.LogError("ICR for {identity} still Requires MasterKey Encryption Upgrade - skipping", icr.OdinId);
                        continue;
                    }

                    // Re-create the circle grant so
                    var keyStoreKey = icr.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
                    icr.PeerKeyStore.CircleGrants[circleKey] =
                        await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDef,
                            new MasterKeyStorageKeySource(masterKey), odinContext);
                    keyStoreKey.Wipe();
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture i
                    // invalidMembers.Add(odinId);
                }

                await this.SaveIcrAsync(icr, odinContext);
            }

            logger.LogDebug("UpdateCircleDefinition '{circle}': {count} member(s) re-granted in {elapsed}ms",
                circleDef.Name, memberIndex, swCircle.ElapsedMilliseconds);

            await circleMembershipService.UpdateAsync(circleDef, odinContext);

            await mediator.Publish(new CircleDefinitionChangedNotification
            {
                OdinContext = odinContext,
                CircleId = circleDef.Id.Value,
                Change = CircleDefinitionChangeType.Updated,
            });

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        /// <summary>
        /// Hands a circle that belongs to no app to one that exists, so its enrollments have someone to
        /// claim them.
        /// </summary>
        /// <remarks>
        /// Circles predating app ownership carry no AppId, which reads as "the owner's own".  That is the
        /// right default and wrong for the ones that were always meant to be an app's: an app is refused
        /// them (<see cref="EnrollInCircleInternalAsync"/>) and never offered them
        /// (<c>CircleMembershipService.GetCircleDefinitions</c>), so nothing an app does can ever involve
        /// them.  Adoption is how the owner corrects that.
        /// <para>
        /// Owner console only, and deliberately not <c>OwnerOrApp</c>.  An app is the owner acting, so
        /// were this open to apps any app could hand itself a circle and quietly widen its own reach --
        /// which is the exact thing the ban on reassignment through a PUT exists to prevent.  The check
        /// is on which app is acting rather than on who the caller is, since only the former separates
        /// the console from an app.
        /// </para>
        /// <para>
        /// No re-granting, unlike <see cref="UpdateCircleDefinitionAsync"/>: ownership names who may
        /// administer the circle and complete its enrollments, and touches neither its drive grants nor
        /// its permissions, so no member's access changes and no grant has to be reminted.
        /// </para>
        /// </remarks>
        public async Task SetCircleOwningAppAsync(GuidId circleId, Guid appId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsOwner();

            if (odinContext.Caller.OdinClientContext?.AppId != null)
            {
                throw new OdinSecurityException(
                    $"An app cannot set the owning app of circle {circleId}; only the owner console can");
            }

            OdinValidationUtils.AssertNotEmptyGuid(appId, nameof(appId));

            // Checked before the write rather than trusted: an AppId naming no app would leave the
            // circle in the one state adoption exists to escape -- owned by something that can never
            // come back for it, and no longer adoptable, since a second call is refused.
            var app = await appRegistrationService.GetAppRegistration(appId, odinContext);
            if (app == null)
            {
                throw new OdinClientException($"No app is registered with id {appId}",
                    OdinClientErrorCode.AppNotRegistered);
            }

            var circle = await circleDefinitionService.GetCircleAsync(circleId);
            if (circle == null)
            {
                throw new OdinClientException($"Circle {circleId} does not exist",
                    OdinClientErrorCode.CircleNotFound);
            }

            await using var tx = await db.BeginStackedTransactionAsync();

            await circleDefinitionService.SetOwningAppAsync(circleId, appId);

            // Ownership and the ability to act on it, together or not at all: a half-applied adoption
            // is an app named on a circle it cannot grant, which is the state this exists to prevent.
            var granted = await GrantAppTheCirclesDrivesAsync(appId, circle, odinContext);

            tx.Commit();

            logger.LogInformation("Circle {circleId} adopted by app {appName} ({appId}); {granted} drive grant(s) added",
                circleId, app.Name, appId, granted.Count);

            await mediator.Publish(new CircleDefinitionChangedNotification
            {
                OdinContext = odinContext,
                CircleId = circleId.Value,
                Change = CircleDefinitionChangeType.Updated,
            });
        }


        /// <summary>
        /// Gives the app the drive access the circle's own grants require, so that owning the circle
        /// means being able to grant it.
        /// </summary>
        /// <remarks>
        /// Naming an app as owner does not let it do the job.  Completing an enrollment mints a circle
        /// grant, which escrows each drive's storage key for the member, and a key cannot be escrowed
        /// by someone who cannot obtain it -- so an app without read on the circle's drives owns a
        /// circle it can never act on, and every enrollment sits in the queue until the owner sweeps
        /// it.  The master key is in scope here (this runs owner-console-only), which is precisely
        /// what makes the escrow possible, so this is the one moment where the gap can be closed.
        /// <para>
        /// The circle states its own requirements: each of its drive grants names a drive and a
        /// permission, and that tuple is exactly what <see cref="CallerCanGrantCircleAsync"/> checks
        /// later.  Copying it satisfies both of that method's branches by construction -- Read brings
        /// the storage key, anything else needs only the matching permission -- so nothing here is a
        /// heuristic about what the app "probably" needs.
        /// </para>
        /// <para>
        /// A union, never a replacement.  <c>UpdateAppPermissionsAsync</c> rebuilds the whole key
        /// store from the drive list it is handed, so sending only the circle's drives would silently
        /// revoke every other grant the app holds.  Permissions are OR'd per drive for the same
        /// reason: an app holding Write on a drive whose circle grants Read must end with both.
        /// </para>
        /// <para>
        /// Returns the drives whose access actually changed, so a caller can report what it did.  An
        /// app that already had everything is left completely alone -- no rewrite, no cache reset.
        /// </para>
        /// </remarks>
        private async Task<List<PermissionedDrive>> GrantAppTheCirclesDrivesAsync(Guid appId,
            CircleDefinition circleDefinition, IOdinContext odinContext)
        {
            var required = (circleDefinition.DriveGrants ?? []).ToList();
            if (required.Count == 0)
            {
                return [];
            }

            var app = await appRegistrationService.GetAppRegistration(appId, odinContext);
            if (app == null)
            {
                throw new OdinClientException($"No app is registered with id {appId}",
                    OdinClientErrorCode.AppNotRegistered);
            }

            // Keyed on the drive, so the union is per drive rather than per grant.
            var merged = new Dictionary<TargetDrive, DrivePermission>();
            foreach (var existing in app.Grant?.DriveGrants ?? [])
            {
                merged[existing.PermissionedDrive.Drive] = existing.PermissionedDrive.Permission;
            }

            var changed = new List<PermissionedDrive>();
            foreach (var req in required)
            {
                var drive = req.PermissionedDrive.Drive;
                var wanted = req.PermissionedDrive.Permission;

                var current = merged.TryGetValue(drive, out var p) ? p : DrivePermission.None;
                if ((current & wanted) == wanted)
                {
                    continue;
                }

                merged[drive] = current | wanted;
                changed.Add(new PermissionedDrive { Drive = drive, Permission = merged[drive] });
            }

            if (changed.Count == 0)
            {
                return [];
            }

            await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
            {
                AppId = appId,
                // Untouched: this is about drive access, and rewriting the permission set from a
                // redacted copy risks dropping a key the app was granted elsewhere.
                PermissionSet = app.Grant?.PermissionSet ?? new PermissionSet(),
                Drives = merged.Select(kv => new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive { Drive = kv.Key, Permission = kv.Value }
                }).ToList()
            }, odinContext);

            logger.LogInformation(
                "Granted app {appId} access to {count} drive(s) so it can grant circle {circleId}: {drives}",
                appId, changed.Count, circleDefinition.Id, string.Join(", ", changed.Select(d => d.ToString())));

            return changed;
        }

        /// <summary>
        /// Moves a circle from the app that owns it to another.  The escape hatch out of
        /// <see cref="SetCircleOwningAppAsync"/>'s one-way rule.
        /// </summary>
        /// <remarks>
        /// Master key required, not merely owner: an app is the owner acting, and no app should be
        /// able to move a circle -- least of all to itself.  Requiring the key is the sharpest way to
        /// say "owner console only" for an operation with no undo but doing it again.
        /// <para>
        /// The queue rewrite is the whole reason this is not just a definition write.
        /// <c>PendingEnrollment.OwningAppId</c> is denormalised from the definition, and
        /// <see cref="ProcessPendingEnrollmentsForAppAsync"/> filters on that copy -- so an entry left
        /// pointing at the previous app is looked at only by the previous app, which can no longer
        /// source the circle's drive keys and therefore skips it. Nobody else ever sees it. Left
        /// alone, moving a circle silently strands every enrollment queued against it.
        /// </para>
        /// <para>
        /// Both writes are one transaction. Half of this is worse than none: the definition moved
        /// with the copies left behind is exactly the stranded state, and the copies moved without
        /// the definition points them at an app the circle does not belong to.
        /// </para>
        /// </remarks>
        public async Task<int> ReassignCircleOwningAppAsync(GuidId circleId, Guid appId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            OdinValidationUtils.AssertNotEmptyGuid(appId, nameof(appId));

            var app = await appRegistrationService.GetAppRegistration(appId, odinContext);
            if (app == null)
            {
                throw new OdinClientException($"No app is registered with id {appId}",
                    OdinClientErrorCode.AppNotRegistered);
            }

            var circle = await circleDefinitionService.GetCircleAsync(circleId);
            if (circle == null)
            {
                throw new OdinClientException($"Circle {circleId} does not exist",
                    OdinClientErrorCode.CircleNotFound);
            }

            var previousAppId = circle.AppId;

            await using var tx = await db.BeginStackedTransactionAsync();

            await circleDefinitionService.ReassignOwningAppAsync(circleId, appId);

            // The app receiving it needs the same access adoption grants. The app losing it keeps
            // what it has: it may well need those drives for reasons that have nothing to do with
            // this circle, and guessing which grants existed only to serve it would be exactly that.
            await GrantAppTheCirclesDrivesAsync(appId, circle, odinContext);

            var entriesRepointed = 0;
            string cursor = null;
            do
            {
                var page = await GetConnectionsInternalAsync(int.MaxValue, cursor, ConnectionStatus.Connected,
                    odinContext);
                cursor = page.Cursor;

                foreach (var icr in page.Results)
                {
                    if (!(icr.PeerKeyStore?.HasPendingEnrollments ?? false))
                    {
                        continue;
                    }

                    var stale = icr.PeerKeyStore.PendingEnrollments
                        .Where(p => p.CircleId == circleId && p.OwningAppId != appId)
                        .ToList();

                    if (stale.Count == 0)
                    {
                        continue;
                    }

                    foreach (var entry in stale)
                    {
                        entry.OwningAppId = appId;
                        entriesRepointed++;
                    }

                    await SaveIcrAsync(icr, odinContext);
                }
            } while (!string.IsNullOrEmpty(cursor));

            tx.Commit();

            logger.LogInformation(
                "Circle {circleId} moved from app {previousAppId} to {appName} ({appId}); " +
                "{entriesRepointed} pending enrollment(s) re-pointed",
                circleId, previousAppId, app.Name, appId, entriesRepointed);

            await mediator.Publish(new CircleDefinitionChangedNotification
            {
                OdinContext = odinContext,
                CircleId = circleId.Value,
                Change = CircleDefinitionChangeType.Updated,
            });

            return entriesRepointed;
        }

        public async Task<List<OdinId>> GetInvalidMembersOfCircleDefinition(CircleDefinition circleDef, IOdinContext odinContext)
        {
            await circleMembershipService.AssertValidDriveGrantsAsync(circleDef.DriveGrants);

            var members = await GetCircleMembersAsync(circleDef.Id, odinContext);

            var invalid = new List<OdinId>();
            foreach (var odinId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                var hasCg = icr.PeerKeyStore.CircleGrants.TryGetValue(circleDef.Id, out _);

                if (icr.IsConnected() && !hasCg)
                {
                    invalid.Add(icr.OdinId);
                }
            }

            return invalid;
        }

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        public async Task DeleteCircleDefinitionAsync(GuidId circleId, IOdinContext odinContext)
        {
            var members = await this.GetCircleMembersAsync(circleId, odinContext);

            if (members.Any())
            {
                throw new OdinClientException("Cannot delete a circle with members", OdinClientErrorCode.CannotDeleteCircleWithMembers);
            }

            await circleMembershipService.DeleteAsync(circleId, odinContext);

            await mediator.Publish(new CircleDefinitionChangedNotification
            {
                OdinContext = odinContext,
                CircleId = circleId.Value,
                Change = CircleDefinitionChangeType.Deleted,
            });
        }

        public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            if (notification.IsNewDrive)
            {
                await HandleDriveAdded(notification.Drive, odinContext);
            }
            else
            {
                await HandleDriveUpdated(notification.Drive, odinContext);
            }
        }

        public async Task Handle(AppRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            await this.ReconcileAuthorizedCircles(notification.OldAppRegistration?.Redacted(), notification.NewAppRegistration.Redacted(),
                odinContext);
        }

        public async Task RevokeConnectionAsync(OdinId odinId, IOdinContext odinContext)
        {
            await Benchmark.MillisecondsAsync(logger, "RevokeConnectionAsync:DeleteAsync",
                async () => { await circleNetworkStorage.DeleteAsync(odinId); });

            await Benchmark.MillisecondsAsync(logger, "RevokeConnectionAsync:Publish", async () =>
            {
                await mediator.Publish(new ConnectionDeletedNotification()
                {
                    OdinId = odinId,
                    OdinContext = odinContext,
                });
            });
        }

        public async Task<IcrTroubleshootingInfo> GetTroubleshootingInfoAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            // Need to see if the circle has the correct drives
            //
            // SystemCircleConstants.ConnectedIdentitiesSystemCircleInitialDrives

            var info = new IcrTroubleshootingInfo();
            var circleDefinitions = (await circleDefinitionService.GetCirclesAsync(true)).ToList();
            var icr = await GetIdentityConnectionRegistrationInternalAsync(odinId);

            info.Icr = icr.Redacted();

            ArgumentNullException.ThrowIfNull(icr);
            ArgumentNullException.ThrowIfNull(icr.PeerKeyStore);
            ArgumentNullException.ThrowIfNull(icr.PeerKeyStore.CircleGrants);

            // Get all circles on identity
            foreach (var definition in circleDefinitions)
            {
                ArgumentNullException.ThrowIfNull(definition);

                var isCircleMember = icr.PeerKeyStore.CircleGrants.TryGetValue(definition.Id, out var circleGrant);
                var hasCircleGrant = circleGrant != null;

                var summary = isCircleMember ? "Identity is in this circle" : "Identity is not a member of this circle";
                var actualPermissionKeys = circleGrant?.PermissionSet?.Redacted() ?? new RedactedPermissionSet() { Keys = [] };
                var permissionKeysMatch = definition.Permissions.Keys.Order().SequenceEqual(actualPermissionKeys.Keys.Order());

                var ci = new CircleInfo()
                {
                    CircleDefinitionId = definition.Id,
                    CircleDefinitionName = definition.Name,
                    CircleDefinitionDriveGrantCount = definition.DriveGrants?.Count() ?? 0,
                    Analysis = new CircleAnalysis()
                    {
                        IsCircleMember = isCircleMember,
                        Summary = summary,
                        PermissionKeysAreValid = permissionKeysMatch,
                        ExpectedPermissionKeys = definition.Permissions.Redacted(),
                        ActualPermissionKeys = actualPermissionKeys,
                        DriveGrantAnalysis = new List<DriveGrantInfo>(),
                    }
                };

                if (isCircleMember && definition.DriveGrants != null && hasCircleGrant)
                {
                    foreach (var expectedDriveGrant in definition.DriveGrants)
                    {
                        var driveId = expectedDriveGrant.PermissionedDrive.Drive.Alias;
                        var driveInfo = await driveManager.GetDriveAsync(driveId);

                        var grantedDrive = circleGrant.KeyStoreKeyEncryptedDriveGrants.SingleOrDefault(dg =>
                            dg.PermissionedDrive == expectedDriveGrant.PermissionedDrive);

                        //you must have drive-read permission to get the Key Store Key
                        // you can have write permission w/o having the storage key
                        var driveIsGranted = grantedDrive != null;
                        var encryptedKeyLength = grantedDrive?.KeyStoreKeyEncryptedStorageKey?.KeyEncrypted?.Length ?? 0;

                        var expectedDrivePermission = expectedDriveGrant.PermissionedDrive.Permission;
                        var actualDrivePermission = grantedDrive?.PermissionedDrive.Permission ?? DrivePermission.None;

                        var drivePermissionIsValid = expectedDrivePermission == actualDrivePermission;
                        var hasValidEncryptionKey = true;
                        if (expectedDrivePermission.HasFlag(DrivePermission.Read))
                        {
                            hasValidEncryptionKey = encryptedKeyLength > 0;
                        }

                        var isValid = driveIsGranted && drivePermissionIsValid && hasValidEncryptionKey;
                        var dgi = new DriveGrantInfo()
                        {
                            DriveName = driveInfo.Name,
                            TargetDrive = driveInfo.TargetDriveInfo,
                            DrivePermissionIsValid = drivePermissionIsValid,
                            HasValidEncryptionKey = hasValidEncryptionKey,
                            DriveGrantIsValid = isValid,
                            DriveIsGranted = driveIsGranted,
                            ExpectedDrivePermission = expectedDrivePermission,
                            ActualDrivePermission = actualDrivePermission,
                            EncryptedKeyLength = encryptedKeyLength
                        };

                        ci.Analysis.DriveGrantAnalysis.Add(dgi);
                    }
                }

                info.Circles.Add(ci);
            }

            return info;
        }

        public async Task ReconcileAuthorizedCircles(RedactedAppRegistration oldAppRegistration, RedactedAppRegistration newAppRegistration,
            IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var appKey = newAppRegistration.AppId.Value;

            await using var tx = await db.BeginStackedTransactionAsync();

            if (null != oldAppRegistration)
            {
                var circlesToRevoke = oldAppRegistration.AuthorizedCircles.Except(newAppRegistration.AuthorizedCircles);
                //TODO: spin thru circles to revoke an update members

                foreach (var circleId in circlesToRevoke)
                {
                    //get all circle members and update their grants
                    var members = await this.GetCircleMembersAsync(circleId, odinContext);

                    foreach (var odinId in members)
                    {
                        var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                        icr.PeerKeyStore.AppGrants[appKey]?.Remove(circleId);
                        await this.SaveIcrAsync(icr, odinContext);
                    }
                }
            }

            foreach (var circleId in newAppRegistration.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await this.GetCircleMembersAsync(circleId, odinContext);

                foreach (var odinId in members)
                {
                    var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                    if (await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext))
                    {
                        // refetch the record since the above method just writes to db
                        icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                    }

                    if (!icr.PeerKeyStore.AppGrants.TryGetValue(appKey, out var appCircleGrantDictionary))
                    {
                        appCircleGrantDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    var keyStoreKey = icr.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
                    var appCircleGrant = await this.CreateAppCircleGrantAsync(newAppRegistration, keyStoreKey, circleId,
                        new MasterKeyStorageKeySource(masterKey));
                    appCircleGrantDictionary[appCircleGrant.CircleId] = appCircleGrant;
                    keyStoreKey.Wipe();

                    icr.PeerKeyStore.AppGrants[appKey] = appCircleGrantDictionary;
                    await this.SaveIcrAsync(icr, odinContext);
                }
            }
            //

            tx.Commit();
        }

        public async Task<VerifyConnectionResponse> GetCallerVerificationHashAsync(IOdinContext odinContext)
        {
            if (!odinContext.Caller.IsConnected)
            {
                logger.LogDebug("Verification Connection Code - not connected, " +
                                "returning null hash.(AuthContext:{ac})",
                    odinContext.AuthContext);

                return new VerifyConnectionResponse
                {
                    IsConnected = false,
                    Hash = null
                };
            }

            // Look up the verification hash on the caller's icr.
            // Skip the lazy encryption upgrade: this is a peer-context call (caller is the
            // remote identity, not the owner) and the upgrade path requires our own ICR key
            // which is not in scope here. Reading the verification hash does not need the
            // encrypted CAT — only the hash byte field — so the upgrade is unnecessary.
            // The upgrade will happen the next time the owner reads the ICR.
            var callerIcr = await this.GetIcrAsync(odinContext.GetCallerOdinIdOrFail(), odinContext,
                overrideHack: true, tryUpgradeEncryption: false);

            if (callerIcr.VerificationHash.IsNullOrEmpty())
            {
                throw new OdinSecurityException("Cannot verify caller");
            }

            var result = new VerifyConnectionResponse()
            {
                IsConnected = callerIcr.IsConnected(),
                Hash = callerIcr.VerificationHash
            };

            return result;
        }

        /// <summary>
        /// Records the owner's review of a connection: enrolls the circles the owner chose and stamps
        /// <see cref="IdentityConnectionRegistration.ReviewedAt"/>, as one act.
        /// </summary>
        /// <remarks>
        /// docs/connection-defaults.md, "On verify".  Nothing is removed -- whatever the connection already
        /// holds from auto-connect stays -- and the stamp is set once: a later review may enroll more
        /// circles but never moves the original timestamp, which is the date clients show the relationship
        /// from.
        /// </remarks>
        public async Task MarkReviewedAsync(OdinId odinId, IEnumerable<GuidId> circleIds, IOdinContext odinContext)
        {
            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinClientException("Cannot review an identity that is not connected",
                    OdinClientErrorCode.IdentityMustBeConnected);
            }

            // What was already waiting before this review, so only what it adds gets announced.
            var alreadyQueued = (icr.PeerKeyStore?.PendingEnrollments ?? [])
                .Select(p => p.CircleId.Value)
                .ToList();

            await using var tx = await db.BeginStackedTransactionAsync();

            if (odinContext.Caller.HasMasterKey)
            {
                // The owner is present, so take the opportunity to re-mint whatever an app had to leave
                // weakly encrypted when it accepted on their behalf.
                await UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);
                await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext);
            }

            foreach (var circleId in circleIds ?? [])
            {
                // The review is the owner's act, and they may well have chosen circles belonging to apps
                // this client cannot serve. Those are recorded rather than refused -- rejecting the whole
                // review because one app's toggle was out of reach would lose the rest of their choices
                // along with it.
                await EnrollInCircleInternalAsync(circleId, odinId, odinContext, enqueueWhenOutOfReach: true);
            }

            // Stamped last: enrollment rewrites the whole row, so a stamp written before it would be
            // carried forward from a stale in-memory copy and lost.
            await StampReviewedIfUnsetAsync(odinId);

            tx.Commit();

            // After the commit, never before: an app told to come and do work that a rollback then erased
            // would arrive to find nothing, and the queue is what makes this safe to send late.
            await PublishPendingEnrollmentNotificationsAsync(odinId, alreadyQueued, odinContext);

            // Peer contexts are cached for an hour keyed on the caller's token; without this the contact
            // keeps running under their pre-review grants long after the owner acted.
            await odinContextCache.ResetAsync();
        }

        /// <summary>
        /// Stamps the review on a connection unless it already carries one.
        /// </summary>
        /// <remarks>
        /// Set-once by design: a second review may enroll more circles, but the timestamp keeps saying when
        /// the owner first vouched for this contact.
        /// <para>
        /// Makes no permission check of its own, and neither do its callers: the review endpoints and the
        /// accept of an incoming connection request are both reached only by the owner or an app, gated at
        /// the route, and both are the owner's own act -- the accept being the review happening at accept
        /// time (docs/connection-defaults.md, "On verify").
        /// </para>
        /// </remarks>
        public async Task StampReviewedIfUnsetAsync(OdinId odinId)
        {
            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (icr == null || !icr.IsConnected() || icr.ReviewedAt != null)
            {
                return;
            }

            await circleNetworkStorage.UpdateReviewedAtAsync(odinId, icr.Status, UnixTimeUtc.Now());
        }

        /// <summary>
        /// Tells each app that a review has left it enrollments only it can complete.
        /// </summary>
        /// <remarks>
        /// Read back from the committed record rather than from what the loop believed it wrote, and
        /// filtered to what this review actually added, so a second review of the same contact does not
        /// re-announce work an app has already been told about. An owner circle names no app and is
        /// skipped; it waits for the owner regardless.
        /// </remarks>
        private async Task PublishPendingEnrollmentNotificationsAsync(OdinId odinId, List<Guid> alreadyQueued,
            IOdinContext odinContext)
        {
            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            foreach (var entry in icr?.PeerKeyStore?.PendingEnrollments ?? [])
            {
                if (!entry.OwningAppId.HasValue || alreadyQueued.Contains(entry.CircleId.Value))
                {
                    continue;
                }

                await mediator.Publish(new PendingEnrollmentsAwaitingNotification
                {
                    OdinContext = odinContext,
                    TargetAppId = entry.OwningAppId.Value,
                    OdinId = odinId,
                    CircleId = entry.CircleId.Value
                });
            }
        }

        /// <summary>
        /// Clears the owner's review of a connection ("un-review"), returning it to New.
        /// </summary>
        /// <remarks>
        /// Rejected while the contact holds a personal-circle membership that a review is what grants:
        /// docs/connection-defaults.md -- "circleIdList ACLs check membership, not tier, so membership must
        /// imply review".
        /// <para>
        /// Two kinds of membership are deliberately excluded from that check, because counting them would
        /// make the clear unreachable rather than safe.  Ambient circles: a
        /// <see cref="CircleGrantOn.Connect"/> circle is by definition granted without any review, and every
        /// shipped one is also <see cref="CircleDesignation.Personal"/> (see
        /// <c>BuiltinCircles.ChatCircle</c>), so every auto-connection would be permanently un-clearable.
        /// The two system circles: they carry <see cref="CircleDesignation.Personal"/> only because that is
        /// the column default, and every connection is in one of them, so the guard would reject every
        /// clear there is.  They are the frozen platform bundles this series retires, assigned by the
        /// pipeline rather than chosen by the owner.
        /// </para>
        /// <para>
        /// Nothing is revoked here.  Clearing the stamp only withdraws the owner's vouching; to take
        /// capability away, revoke the circles.
        /// </para>
        /// </remarks>
        public async Task ClearReviewAsync(OdinId odinId, IOdinContext odinContext)
        {
            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (icr == null)
            {
                throw new OdinClientException($"No connection found for {odinId}", OdinClientErrorCode.NotAConnectedIdentity);
            }

            if (icr.ReviewedAt == null)
            {
                return;
            }

            var memberships = (icr.PeerKeyStore?.CircleGrants?.Keys ?? (ICollection<Guid>)Array.Empty<Guid>())
                .Concat(icr.PeerKeyStore?.DepositedGrants?.Select(d => d.CircleId.Value) ?? [])
                .Distinct();

            var blocking = new List<BlockingCircle>();

            foreach (var circleId in memberships)
            {
                if (SystemCircleConstants.IsSystemCircle(circleId))
                {
                    continue;
                }

                var definition = await circleDefinitionService.GetCircleAsync(circleId);
                if (definition is { Designation: CircleDesignation.Personal, GrantOn: not CircleGrantOn.Connect })
                {
                    blocking.Add(new BlockingCircle { CircleId = definition.Id, Name = definition.Name });
                }
            }

            // Every offender, not the first one found: the caller has to remove the contact from all of
            // them before the clear can succeed, so reporting one at a time makes them discover the rest
            // one rejected action at a time.  Ids as well as names, so the client can link to each circle
            // rather than resolve a name that is not unique anyway.
            if (blocking.Count > 0)
            {
                throw new OdinClientException(
                    $"Cannot clear the review for {odinId} while they are a member of the personal circle(s) " +
                    $"{string.Join(", ", blocking.Select(c => $"'{c.Name}'"))}; remove them from those first",
                    OdinClientErrorCode.CannotClearReviewWhilePersonalCircleMember)
                {
                    Extensions = new Dictionary<string, object> { ["blockingCircles"] = blocking }
                };
            }

            await circleNetworkStorage.UpdateReviewedAtAsync(odinId, icr.Status, null);

            await odinContextCache.ResetAsync();
        }

        /// <summary>
        /// Upgrades a connection which was created automatically (i.e. because of an introduction) to a confirmed connection
        /// </summary>
        public async Task ConfirmConnectionAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIcrAsync(odinId, odinContext);

            if (!icr.IsConnected())
            {
                throw new OdinClientException("Cannot confirm identity that is not connected", OdinClientErrorCode.IdentityMustBeConnected);
            }

            if (!icr.PeerKeyStore.CircleGrants.TryGetValue(SystemCircleConstants.AutoConnectionsCircleId, out _))
            {
                throw new OdinClientException("Cannot confirm identity that is not in the AutoConnectionsCircle",
                    OdinClientErrorCode.NotAnAutoConnection);
            }

            await using var tx = await db.BeginStackedTransactionAsync();

            await UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);
            await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext);

            await this.RevokeCircleAccessAsync(SystemCircleConstants.AutoConnectionsCircleId, odinId, odinContext);
            await this.GrantCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinId, odinContext);

            tx.Commit();

            // Peer contexts are cached for an hour keyed on the caller's token, and only the
            // finalized/blocked/deleted notifications reset that cache. Without this the confirmation
            // is invisible to the identity that was just confirmed -- their calls keep running under the
            // auto-connected circle (no AllowIntroductions) long after the owner acted.
            await odinContextCache.ResetAsync();
        }

        public async Task<bool> ClearVerificationHashAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIcrAsync(odinId, odinContext);

            if (!icr.VerificationHash.IsNullOrEmpty())
            {
                await circleNetworkStorage.UpdateVerificationHashAsync(icr.OdinId, icr.Status, []);
                logger.LogDebug("Hash was cleared for identity [{identity}]", icr.OdinId);
                return true;
            }

            return false;
        }


        public async Task<bool> UpdateVerificationHashAsync(OdinId odinId, Guid randomCode, SensitiveByteArray sharedSecret,
            IOdinContext odinContext)
        {
            if (!odinContext.Caller.IsOwner)
            {
                odinContext.Caller.AssertCallerIsConnected();
                OdinValidationUtils.AssertIsTrue(odinId == odinContext.GetCallerOdinIdOrFail(), "caller does not match target identity");
            }

            var icr = await this.GetIcrAsync(odinId, odinContext);

            if (!icr.IsConnected())
            {
                logger.LogDebug("Skipping UpdateVerificationHash -[{icr}] is not connected", icr.OdinId);
                return false;
            }

            // if (icr.VerificationHash.IsNullOrEmpty())
            {
                // this should not occur since this process is running at the same time
                // we introduce the ability to have a null EncryptedClientAccessToken
                // for a connected identity; but #paranoid
                if (icr.EncryptedClientAccessToken == null)
                {
                    logger.LogDebug("Skipping UpdateVerificationHash since connected identity was missing EncryptedClientAccessToken");
                    return false;
                }

                var hash = this.CreateVerificationHash(randomCode, sharedSecret);

                logger.LogDebug("Saving identity [{identity}] with hash [{hash}]", icr.OdinId, hash.ToBase64());

                await circleNetworkStorage.UpdateVerificationHashAsync(icr.OdinId, icr.Status, hash);

                return true;
            }
            //
            // logger.LogDebug("Skipping verification hash update for identity [{identity}] " +
            //                 "called but one is already set [hash:{value}]",
            //     icr.OdinId,
            //     icr.VerificationHash.ToBase64());
            //
            // return false;
        }

        public byte[] CreateVerificationHash(Guid randomCode, SensitiveByteArray sharedSecret)
        {
            var combined = ByteArrayUtil.Combine(randomCode.ToByteArray(), sharedSecret.GetKey());
            var expectedHash = ByteArrayUtil.CalculateSHA256Hash(combined);
            return expectedHash;
        }

        public async Task<ClientAccessToken> CreatePeerIcrClientForCallerAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsConnected();
            var caller = odinContext.GetCallerOdinIdOrFail();

            var grantKeyStoreKey = odinContext.PermissionsContext.GetKeyStoreKey();
            var (accessRegistration, token) = await exchangeGrantService.CreateClientAccessToken(
                grantKeyStoreKey, ClientTokenType.PeerNotificationSubscriber);

            var client = new PeerIcrClient
            {
                Identity = caller,
                ServerHalfOfClientKey = accessRegistration
            };

            await circleNetworkStorage.SavePeerIcrClientAsync(client);
            return token;
        }

        public async Task<PeerIcrClient> GetPeerIcrClientAsync(Guid accessRegId)
        {
            return await circleNetworkStorage.GetPeerIcrClientAsync(accessRegId);
        }

        /// <summary>
        /// Gate for managing circle membership: the owner (master key) or a caller granted
        /// <see cref="PermissionKeys.ManageCircleMembership"/> (e.g. an app).
        /// </summary>
        private static void AssertCanManageCircleMembership(IOdinContext odinContext)
        {
            if (!odinContext.Caller.HasMasterKey)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageCircleMembership);
            }
        }

        /// <summary>
        /// Builds a <see cref="DepositedGrant"/> for the circle: drive storage keys are sourced
        /// from the caller's own permission context (throws for drives beyond its read scope)
        /// and sealed to the store's write-only public key. The caller never touches the store's
        /// key store key and can read nothing back.
        /// </summary>
        /// <summary>
        /// Completes the pending enrollments this caller is able to complete, and leaves the rest.
        /// Returns how many connections were touched and how many enrollments were finished.
        /// </summary>
        /// <remarks>
        /// A pending enrollment is a choice the owner made that nobody present could carry out -- almost
        /// always another app's circle, whose drives only that app can read.  This is the app coming back
        /// to finish its own share of a review someone else's client recorded.
        /// <para>
        /// Owning the circle <i>is</i> the authorization, which is why this asks for no permission of its
        /// own.  The decision was the owner's and was made at review time; the app is carrying it out, not
        /// making it, and a permission about deciding membership has nothing to say about executing a
        /// decision already taken.  An app sees only the entries for circles it owns; the owner console,
        /// scoped to no app, sees them all.
        /// </para>
        /// <para>
        /// Scope is still re-checked per entry rather than trusted from enqueue time, so nothing an app
        /// could not do directly becomes possible by way of the queue.
        /// </para>
        /// <para>
        /// For a read-bearing circle this moves the enrollment one step, not all the way: the app still
        /// cannot reach the Peer Key, so the result is a deposited grant that converts on the contact's
        /// next call, an owner touch, or the upgrade drain.
        /// </para>
        /// </remarks>
        public async Task<(int connectionsProcessed, int enrollmentsCompleted)> ProcessPendingEnrollmentsForAppAsync(
            IOdinContext odinContext)
        {
            // The question is which app is acting, not who the caller is: an app is the owner acting, with
            // less than the owner console has, so neither IsOwner nor HasMasterKey separates the two. An
            // app is scoped to the circles it owns; the owner console, being no app in particular, is not
            // scoped at all.
            var callerAppId = odinContext.Caller.OdinClientContext?.AppId?.Value;

            var connectionsProcessed = 0;
            var enrollmentsCompleted = 0;

            string cursor = null;
            do
            {
                var page = await GetConnectionsInternalAsync(int.MaxValue, cursor, ConnectionStatus.Connected, odinContext);
                cursor = page.Cursor;

                foreach (var icr in page.Results)
                {
                    if (!(icr.PeerKeyStore?.HasPendingEnrollments ?? false))
                    {
                        continue;
                    }

                    var completed = new List<GuidId>();

                    foreach (var entry in icr.PeerKeyStore.PendingEnrollments.ToList())
                    {
                        // Everything about one entry is inside the try, not just the enrolment: reading
                        // the definition and deciding whether this caller can grant it both reach into
                        // the drive manager, which throws for a drive that has gone missing. A throw
                        // there would otherwise abort the whole pass -- every later entry on this
                        // connection and every connection after it -- for one unlucky entry.
                        try
                        {
                            var circleDefinition = await circleDefinitionService.GetCircleAsync(entry.CircleId);

                            if (circleDefinition == null)
                            {
                                // The circle was deleted while the entry waited. There is nothing left to
                                // grant, so drop it rather than keeping an entry that can never complete
                                // -- the same way a deposit for a deleted circle is dropped at conversion.
                                logger.LogDebug("Dropping pending enrollment for {odinId}: circle {circleId} no longer exists",
                                    icr.OdinId, entry.CircleId);
                                completed.Add(entry.CircleId);
                                continue;
                            }

                            // Ownership is read from the definition, never from the entry's copy of it.
                            // PendingEnrollment.OwningAppId is denormalised at enqueue time, so it is
                            // whatever the circle said back then: adopting an unowned circle, or moving
                            // one between apps, leaves every waiting entry pointing at the previous
                            // answer. Filtering on that copy made the owning app process nothing at all,
                            // silently, which is the opposite of what adopting a circle is for. Nothing
                            // is saved by the copy here either -- the definition is loaded either way,
                            // one line above.
                            if (callerAppId != null && circleDefinition.AppId != callerAppId)
                            {
                                logger.LogDebug(
                                    "Skipping pending enrollment for {odinId} in circle {circleId}: it belongs to " +
                                    "app {owningAppId}, not to the calling app {callerAppId}",
                                    icr.OdinId, entry.CircleId, circleDefinition.AppId, callerAppId);
                                continue;
                            }

                            if (!await CallerCanGrantCircleAsync(circleDefinition, odinContext))
                            {
                                // The reason an app that owns the circle still cannot finish the job:
                                // completing it needs the storage keys for the circle's drives, and this
                                // caller cannot source them. Left queued for a caller that can.
                                logger.LogDebug(
                                    "Leaving pending enrollment for {odinId} in circle {circleId} queued: the caller " +
                                    "cannot source the storage keys for its drives",
                                    icr.OdinId, entry.CircleId);
                                continue;
                            }

                            await EnrollInCircleInternalAsync(entry.CircleId, icr.OdinId, odinContext);
                            completed.Add(entry.CircleId);
                            enrollmentsCompleted++;
                        }
                        catch (Exception e)
                        {
                            // Left in place, so it is retried next time rather than being lost.
                            logger.LogWarning(e,
                                "Failed to complete pending enrollment for {odinId} in circle {circleId}; leaving it queued",
                                icr.OdinId, entry.CircleId);
                        }
                    }

                    if (completed.Count == 0)
                    {
                        continue;
                    }

                    // Re-read before stripping: enrollment saved the record itself, so the copy in hand is
                    // now stale and writing it back would undo the grant just made.
                    var current = await GetIdentityConnectionRegistrationInternalAsync(icr.OdinId);
                    current.PeerKeyStore.PendingEnrollments.RemoveAll(p => completed.Any(c => c == p.CircleId));
                    await SaveIcrAsync(current, odinContext);

                    connectionsProcessed++;
                }
            } while (!string.IsNullOrEmpty(cursor));

            return (connectionsProcessed, enrollmentsCompleted);
        }

        /// <summary>
        /// Whether this caller could produce every part of the circle's grant itself.
        /// </summary>
        /// <remarks>
        /// Deliberately mirrors what <see cref="CreateDepositedGrantAsync"/> actually does, rather than
        /// approximating it: a drive grant carrying Read needs the drive's storage key, which is sourced
        /// through <see cref="PermissionContextStorageKeySource"/> and so asks
        /// <c>TryGetDriveStorageKey</c>; one that does not need a key needs only that the caller holds the
        /// permission it is handing out.  Answering differently from the builder would either enqueue work
        /// that would have succeeded, or attempt work that throws.
        /// </remarks>
        private async Task<bool> CallerCanGrantCircleAsync(CircleDefinition circleDefinition, IOdinContext odinContext)
        {
            foreach (var req in circleDefinition.DriveGrants ?? [])
            {
                var drive = await driveManager.GetDriveAsync(req.PermissionedDrive.Drive.Alias, true);
                var permission = req.PermissionedDrive.Permission;

                var needsStorageKey = permission.HasFlag(DrivePermission.Read) ||
                                      permission.HasFlag(DrivePermission.ConditionalTemporalRead);

                if (needsStorageKey)
                {
                    if (!odinContext.PermissionsContext.TryGetDriveStorageKey(drive.Id, out var storageKey))
                    {
                        return false;
                    }

                    storageKey?.Wipe();
                    continue;
                }

                if (!odinContext.PermissionsContext.HasDrivePermission(drive.Id, permission))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Records that the owner asked for a circle nothing present could grant.  Idempotent -- a circle
        /// already enqueued, deposited or granted is left alone.
        /// </summary>
        private void EnqueuePendingEnrollment(IdentityConnectionRegistration icr, CircleDefinition circleDefinition,
            IOdinContext odinContext)
        {
            var circleId = circleDefinition.Id;

            if (icr.PeerKeyStore.CircleGrants.ContainsKey(circleId) ||
                icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleId) ||
                icr.PeerKeyStore.PendingEnrollments.Any(p => p.CircleId == circleId))
            {
                return;
            }

            icr.PeerKeyStore.PendingEnrollments.Add(new PendingEnrollment
            {
                CircleId = circleId,
                OwningAppId = circleDefinition.AppId,
                RequestedByAppId = odinContext.Caller.OdinClientContext?.AppId?.Value,
                Requested = UnixTimeUtc.Now()
            });

            logger.LogDebug(
                "Enqueued pending enrollment for {odinId} in circle {circleId} (owned by app {owningAppId})",
                icr.OdinId, circleId, circleDefinition.AppId);

            // Deliberately silent. Telling the owning app is the caller's job, after its transaction has
            // committed -- announcing work that a rollback would erase is worse than announcing it late.
        }

        /// <summary>
        /// Refuses a grant the caller could not make itself: every drive permission being handed out must
        /// be one the caller already holds on that drive.
        /// </summary>
        /// <remarks>
        /// The write-side half of the scope constraint, mirroring the assertion
        /// <see cref="CreateDepositedGrantAsync"/> makes for the same reason.  The read side needs no
        /// check here because a read grant never reaches this path -- it requires the Peer Key and is
        /// deposited instead.
        /// </remarks>
        private void AssertCallerHoldsGrantedDrivePermissions(CircleDefinition circleDefinition, IOdinContext odinContext)
        {
            foreach (var req in circleDefinition.DriveGrants ?? [])
            {
                var driveId = req.PermissionedDrive.Drive.Alias;
                odinContext.PermissionsContext.AssertHasDrivePermission(driveId, req.PermissionedDrive.Permission);
            }
        }

        private async Task<DepositedGrant> CreateDepositedGrantAsync(PeerKeyStore store, CircleDefinition circleDefinition,
            IOdinContext odinContext)
        {
            if (store.WriteOnlyKeyPair == null)
            {
                throw new OdinClientException("This connection cannot accept deposited grants yet; " +
                                              "its write-only key is provisioned the next time the owner grants a circle on it");
            }

            var storageKeySource = new PermissionContextStorageKeySource(odinContext);

            var deposit = new DepositedGrant
            {
                CircleId = circleDefinition.Id,
                DepositingAppId = odinContext.Caller.OdinClientContext?.AppId?.Value,
                Deposited = UnixTimeUtc.Now(),
                PermissionSet = circleDefinition.Permissions
            };

            foreach (var req in circleDefinition.DriveGrants ?? [])
            {
                var drive = await driveManager.GetDriveAsync(req.PermissionedDrive.Drive.Alias, true);

                bool needsStorageKey = req.PermissionedDrive.Permission.HasFlag(DrivePermission.Read) ||
                                       req.PermissionedDrive.Permission.HasFlag(DrivePermission.ConditionalTemporalRead);

                EccEncryptedPayload sealedStorageKey = null;
                if (needsStorageKey)
                {
                    // The cryptographic scope boundary: throws for any drive the caller cannot read.
                    var storageKey = storageKeySource.GetStorageKey(drive);
                    sealedStorageKey = PeerKeyStoreWriteOnlyKey.Seal(store.WriteOnlyKeyPair, storageKey.GetKey());
                    storageKey.Wipe();
                }
                else
                {
                    // Write-side scope boundary: the depositor must itself hold what it grants.
                    odinContext.PermissionsContext.AssertHasDrivePermission(drive.Id, req.PermissionedDrive.Permission);
                }

                deposit.DriveGrants.Add(new DepositedDriveGrant
                {
                    DriveId = drive.Id,
                    PermissionedDrive = req.PermissionedDrive,
                    SealedStorageKey = sealedStorageKey
                });
            }

            return deposit;
        }

        /// <summary>
        /// Rewrites pending deposited grants as normal Peer-Key-encrypted circle grants; call
        /// whenever the store's key store key is in scope. Re-mints from the current circle
        /// definition (drops deposits for deleted circles; a direct grant made in the meantime
        /// wins). Mutates the store — the caller saves the ICR. Returns the converted circle ids.
        /// </summary>
        private async Task<List<Guid>> ConvertDepositedGrantsAsync(PeerKeyStore store, SensitiveByteArray keyStoreKey,
            IOdinContext odinContext)
        {
            var convertedCircleIds = new List<Guid>();

            if (!store.HasPendingDeposits || store.WriteOnlyKeyPair == null)
            {
                return convertedCircleIds;
            }

            foreach (var deposit in store.DepositedGrants.ToList())
            {
                if (store.CircleGrants.ContainsKey(deposit.CircleId))
                {
                    // granted directly in the meantime; the direct grant wins
                    store.DepositedGrants.Remove(deposit);
                    continue;
                }

                var circleDefinition = await circleDefinitionService.GetCircleAsync(deposit.CircleId);
                if (circleDefinition == null)
                {
                    logger.LogDebug("Dropping deposited grant for deleted circle {circleId}", deposit.CircleId);
                    store.DepositedGrants.Remove(deposit);
                    continue;
                }

                // Sealed keys cover the drives that were in the definition at deposit time;
                // drives added since mint keyless and are healed by the owner's next touch.
                var storageKeySource = new DepositedGrantStorageKeySource(deposit, store.WriteOnlyKeyPair, keyStoreKey);
                var circleGrant =
                    await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDefinition, storageKeySource, odinContext);

                store.CircleGrants.Add(deposit.CircleId, circleGrant);
                store.DepositedGrants.Remove(deposit);
                convertedCircleIds.Add(deposit.CircleId);

                logger.LogDebug("Converted deposited grant for circle {circleId} (deposited by app {appId})",
                    deposit.CircleId, deposit.DepositingAppId);
            }

            return convertedCircleIds;
        }

        /// <summary>
        /// Opportunistic deposit conversion at peer CAT auth — the peer's token reconstructs the
        /// key store key, which is exactly the "next accessed with the key in scope" moment.
        /// Never fails the auth path. Also fans out AppCircleGrants for the converted circles, via
        /// <see cref="NoStorageKeySource"/> since there is no master key here: any drive grant that
        /// doesn't need Read (the common case -- e.g. Chat's Write|React grants) comes out fully
        /// working; one that needs Read comes out keyless (permission recorded, no storage key) and
        /// self-heals the next time <see cref="ReconcileAuthorizedCircles"/> runs for that app (i.e.
        /// the owner changes that app's registration) — same deferred-key pattern already used for
        /// introduction/auto-connect accepts.
        /// </summary>
        private async Task<bool> TryConvertDepositedGrantsAtPeerAuthAsync(IdentityConnectionRegistration icr,
            ClientAuthenticationToken remoteIcrToken, IOdinContext odinContext)
        {
            if (!(icr.PeerKeyStore?.HasPendingDeposits ?? false))
            {
                return false;
            }

            try
            {
                var (keyStoreKey, sharedSecret) = icr.PeerKeyStore.PeerClientKey.DecryptUsingClientAuthenticationToken(remoteIcrToken);
                try
                {
                    var convertedCircleIds = await ConvertDepositedGrantsAsync(icr.PeerKeyStore, keyStoreKey, odinContext);

                    await FanOutAppCircleGrantsAsync(icr, keyStoreKey, convertedCircleIds, NoStorageKeySource.Instance, odinContext);

                    await this.SaveIcrAsync(icr, odinContext);

                    foreach (var convertedCircleId in convertedCircleIds)
                    {
                        await mediator.Publish(new ConnectionChangedNotification
                        {
                            OdinContext = odinContext,
                            OdinId = icr.OdinId,
                            CircleId = convertedCircleId,
                            Change = ConnectionChangeType.CircleGranted,
                        });
                    }

                    return true;
                }
                finally
                {
                    keyStoreKey?.Wipe();
                    sharedSecret?.Wipe();
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed converting deposited grants for {odinId}; auth continues", icr.OdinId);
                return false;
            }
        }

        /// <summary>
        /// For each of <paramref name="grantedCircleIds"/>, finds every registered app that
        /// authorizes that circle (<see cref="RedactedAppRegistration.AuthorizedCircles"/>) and
        /// mints/updates that app's <see cref="AppCircleGrant"/> on <paramref name="icr"/>. Shared
        /// by <see cref="GrantCircleAsync"/> (owner path, real keys via the master key) and
        /// <see cref="TryConvertDepositedGrantsAtPeerAuthAsync"/> (peer-CAT path, no master key —
        /// see that call site for the <see cref="NoStorageKeySource"/> tradeoff). Mutates
        /// <paramref name="icr"/> in place; caller saves.
        /// </summary>
        private async Task FanOutAppCircleGrantsAsync(IdentityConnectionRegistration icr, SensitiveByteArray keyStoreKey,
            IEnumerable<Guid> grantedCircleIds, IStorageKeySource storageKeySource, IOdinContext odinContext)
        {
            // GetAppsGrantingCircleAsync (unlike GetRegisteredAppsAsync) does not assert the master
            // key -- required here since the peer-CAT caller (TryConvertDepositedGrantsAtPeerAuthAsync)
            // never has one.
            foreach (var grantedCircleId in grantedCircleIds)
            {
                var appsThatGrantThisCircle = await appRegistrationService.GetAppsGrantingCircleAsync(grantedCircleId, odinContext);
                foreach (var app in appsThatGrantThisCircle)
                {
                    var appCircleGrant = await this.CreateAppCircleGrantAsync(app, keyStoreKey, grantedCircleId, storageKeySource);
                    icr.PeerKeyStore.AddUpdateAppCircleGrant(appCircleGrant);
                }
            }
        }

        private async Task<AppCircleGrant> CreateAppCircleGrantAsync(
            RedactedAppRegistration appReg,
            SensitiveByteArray keyStoreKey,
            GuidId circleId,
            IStorageKeySource storageKeySource)
        {
            //map the exchange grant to a structure that matches ICR
            //(the KeyStore wrapper is discarded, so no master key wrap is needed here)
            var grant = await exchangeGrantService.CreateExchangeGrantAsync(
                keyStoreKey,
                appReg.CircleMemberPermissionSetGrantRequest.PermissionSet,
                appReg.CircleMemberPermissionSetGrantRequest.Drives,
                storageKeySource,
                masterKey: null);

            return new AppCircleGrant()
            {
                AppId = appReg.AppId,
                CircleId = circleId,
                KeyStoreKeyEncryptedDriveGrants = grant.DriveGrants,
                PermissionSet = grant.PermissionSet,
            };
        }


        private async Task HandleDriveUpdated(StorageDrive drive, IOdinContext odinContext)
        {
            async Task UpdateIfRequired(CircleDefinition def)
            {
                var hasExistingDriveGrant = def.DriveGrants.Any(dg => dg.PermissionedDrive.Drive == drive.TargetDriveInfo);
                if (drive.AllowAnonymousReads == false && hasExistingDriveGrant)
                {
                    //remove the drive as it no longer allows anonymous reads
                    def.DriveGrants = def.DriveGrants.Where(dg => dg.PermissionedDrive.Drive != drive.TargetDriveInfo).ToList();
                    await this.UpdateCircleDefinitionAsync(def, odinContext);
                    return;
                }

                if (drive.AllowAnonymousReads && !hasExistingDriveGrant)
                {
                    //act like it's new
                    await this.HandleDriveAdded(drive, odinContext);
                }
            }

            CircleDefinition confirmedCircle = await circleMembershipService.GetCircleAsync(
                SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);

            await UpdateIfRequired(confirmedCircle);

            CircleDefinition autoConnectedCircle = await circleMembershipService.GetCircleAsync(
                SystemCircleConstants.AutoConnectionsCircleId, odinContext);

            await UpdateIfRequired(autoConnectedCircle);
        }

        /// <summary>
        /// Updates the system circles drive grants
        /// </summary>
        private async Task HandleDriveAdded(StorageDrive drive, IOdinContext odinContext)
        {
            //only add anonymous drives
            if (drive.AllowAnonymousReads == false)
            {
                return;
            }

            async Task GrantAnonymousRead(CircleDefinition def)
            {
                logger.LogDebug("GrantAnonymousRead called for circle {def}", def.Name);
                var swGrant = System.Diagnostics.Stopwatch.StartNew();

                var grants = def.DriveGrants?.ToList() ?? new List<DriveGrantRequest>();
                grants.Add(new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = drive.TargetDriveInfo,
                        Permission = DrivePermission.Read
                    }
                });

                def.DriveGrants = grants;
// Re-grants the circle to every existing member, so this is where a large identity spends its
                // time -- and where the drive-added handler was seen to stop with no matching log line.
                await this.UpdateCircleDefinitionAsync(def, odinContext);
                logger.LogDebug("GrantAnonymousRead finished for circle {def} in {elapsed}ms", def.Name,
                    swGrant.ElapsedMilliseconds);
            }

            // System circles may not exist yet — e.g. a tenant that creates a drive before
            // /config/system/initialize has run. Skip with a warning instead of NRE-ing in
            // GrantAnonymousRead. The drive is still inserted; it just won't get an
            // anonymous-read grant on the (still-uncreated) system circles. Standard onboarding
            // calls initialize before any drive create so this is an edge case.
            var confirmedCircle = await
                circleMembershipService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);
            if (confirmedCircle != null)
            {
                await GrantAnonymousRead(confirmedCircle);
            }
            else
            {
                logger.LogWarning(
                    "HandleDriveAdded: ConfirmedConnections system circle missing — drive {drive} added without anonymous-read grant. Run /config/system/initialize to create system circles.",
                    drive.TargetDriveInfo.Alias);
            }

            var autoConnectedCircle = await
                circleMembershipService.GetCircleAsync(SystemCircleConstants.AutoConnectionsCircleId, odinContext);
            if (autoConnectedCircle != null)
            {
                await GrantAnonymousRead(autoConnectedCircle);
            }
            else
            {
                logger.LogWarning(
                    "HandleDriveAdded: AutoConnections system circle missing — drive {drive} added without anonymous-read grant. Run /config/system/initialize to create system circles.",
                    drive.TargetDriveInfo.Alias);
            }
        }


        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternalAsync(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            ServerHalfOfClientKey accessReg,
            bool applyAppCircleGrants,
            IOdinContext odinContext)
        {
            // Note: the icr.AccessGrant.AccessRegistration and parameter accessReg might not be the same in the case of YouAuth; this is intentional


            var (grants, enabledCircles) = await
                circleMembershipService.MapCircleGrantsToExchangeGrantsAsync(icr.OdinId.AsciiDomain,
                    icr.PeerKeyStore.CircleGrants.Values.ToList(), odinContext);

            if (applyAppCircleGrants)
            {
                foreach (var kvp in icr.PeerKeyStore.AppGrants)
                {
                    // var appId = kvp.Key;
                    var appCircleGrantDictionary = kvp.Value;

                    foreach (var (_, appCg) in appCircleGrantDictionary)
                    {
                        var alreadyEnabledCircle = enabledCircles.Exists(cid => cid == appCg.CircleId);
                        if (alreadyEnabledCircle || await circleDefinitionService.IsEnabledAsync(appCg.CircleId))
                        {
                            if (!alreadyEnabledCircle)
                            {
                                enabledCircles.Add(appCg.CircleId);
                            }

                            if (grants.ContainsKey(kvp.Key))
                            {
                                //TODO: figuring out a production issue
                                if (grants.TryGetValue(kvp.Key, out var v))
                                {
                                    var existingKeyJson = OdinSystemSerializer.Serialize(v.Redacted());
                                    var newKeyJson = OdinSystemSerializer.Serialize(appCg);

                                    var message = $"Key with value [{kvp.Key}] already exists in grants.";
                                    message += $"\n Existing key has [{existingKeyJson}]";
                                    message += $"\n AppGrant Key [{newKeyJson}]";

                                    logger.LogDebug(message);
                                }
                                else
                                {
                                    logger.LogDebug(
                                        $"Wild; so wild. grants.ContainsKey says it has {kvp.Key} but grants.TryGetValues does not???");
                                }
                            }
                            else
                            {
                                grants.Add(kvp.Key, new KeyStore()
                                {
                                    Created = 0,
                                    Modified = 0,
                                    IsRevoked = false, //TODO
                                    DriveGrants = appCg.KeyStoreKeyEncryptedDriveGrants,
                                    MasterKeyEncryptedKeyStoreKey = null, //not required since this is not being created for the owner
                                    PermissionSet = appCg.PermissionSet
                                });
                            }
                        }
                    }
                }
            }


            //TODO: only add this if I follow this identity and this is for transit
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var feedDriveWriteGrant = await exchangeGrantService.CreateExchangeGrantAsync(keyStoreKey, new Permissions_PermissionSet(),
                new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = WellKnownAppDrives.FeedDrive,
                            Permission = DrivePermission.Write
                        }
                    }
                },
                NoStorageKeySource.Instance,
                masterKey: null,
                icrKey: null);

            grants.Add(ByteArrayUtil.ReduceSHA256Hash("feed_drive_writer"), feedDriveWriteGrant);

            var permissionKeys = tenantContext.Settings.GetAdditionalPermissionKeysForConnectedIdentities();
            var anonDrivePermissions = tenantContext.Settings.GetAnonymousDrivePermissionsForConnectedIdentities();

            var permissionCtx = await exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                odinContext: odinContext,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            var result = (permissionCtx, enabledCircles);
            return result;
        }


        private async Task<CursoredResult<IdentityConnectionRegistration>> GetConnectionsInternalAsync(int count, string cursor,
            ConnectionStatus status,
            IOdinContext odinContext)
        {
            var (list, nextCursor) = await circleNetworkStorage.GetListAsync(count, cursor, status);
            return new CursoredResult<IdentityConnectionRegistration>()
            {
                Cursor = nextCursor,
                Results = list
            };
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="registration">The connection info to be checked</param>
        /// <returns></returns>
        private void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration)
        {
            if (registration.Status == ConnectionStatus.Blocked)
            {
                throw new OdinSecurityException("OdinId is blocked");
            }
        }

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternalAsync(OdinId odinId)
        {
            var registration = await circleNetworkStorage.GetAsync(odinId);

            if (null == registration)
            {
                return new IdentityConnectionRegistration()
                {
                    OdinId = odinId,
                    Status = ConnectionStatus.None,
                    LastUpdated = -1
                };
            }

            return registration;
        }

        private async Task SaveIcrAsync(IdentityConnectionRegistration icr, IOdinContext odinContext)
        {
            var lockKey = NodeLockKey.Create(nameof(CircleNetworkService) + ":" + odinContext?.Tenant);
            await using (await nodeLock.LockAsync(lockKey))
            {
                //TODO: this is a critical change; need to audit this
                if (icr.Status == ConnectionStatus.None)
                {
                    await circleNetworkStorage.DeleteAsync(icr.OdinId);
                }
                else
                {
                    await circleNetworkStorage.UpsertAsync(icr, odinContext);
                }
            }
        }

        public async Task UpgradeTokenEncryptionIfNeededAsync(IdentityConnectionRegistration identity, IOdinContext odinContext)
        {
            if (identity.TemporaryWeakClientAccessToken != null && identity.EncryptedClientAccessToken == null)
            {
                logger.LogDebug("Upgrading ICR Token Encryption for {id}", identity.OdinId);

                var keyStoreKey = await publicPrivateKeyService.EccDecryptPayload(identity.TemporaryWeakClientAccessToken, odinContext);

                var unencryptedCat = ClientAccessToken.FromPortableBytes(keyStoreKey);
                var rawIcrKey = odinContext.PermissionsContext.GetIcrKey();
                var encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, unencryptedCat);

                await circleNetworkStorage.UpdateClientAccessTokenAsync(identity.OdinId, identity.Status, encryptedCat);
            }
        }

        /// <summary>
        /// Attempts to upgrade the identity's master-key store-key encryption if it still requires it
        /// (i.e. the grant was established without the owner's master key). Returns true when the identity
        /// no longer requires the upgrade and is therefore safe to (re)grant circles to. Returns false when
        /// the upgrade could not be completed (e.g. the identity has no <see cref="IdentityConnectionRegistration.TempWeakKeyStoreKey"/>
        /// to recover from), in which case the caller should skip it and retry once the identity is upgraded.
        /// </summary>
        public async Task<bool> TryUpgradeMasterKeyStoreKeyEncryptionAsync(IdentityConnectionRegistration identity,
            IOdinContext odinContext)
        {
            if (!identity.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
            {
                return true;
            }

            await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(identity, odinContext);

            // The upgrade writes to the db, so refetch to determine whether it actually took effect.
            var refreshed = await GetIdentityConnectionRegistrationInternalAsync(identity.OdinId);
            return !refreshed.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade();
        }

        /// <summary>
        /// Iterates all connected identities and (1) upgrades the master-key store-key encryption for any
        /// that still require it (i.e. their grant was established without the owner's master key), and (2)
        /// backfills the write-only <see cref="PeerKeyStore.WriteOnlyKeyPair"/> for any that predate it, so
        /// apps can deposit circle grants via <see cref="CreateDepositedGrantAsync"/> without waiting for the
        /// owner to first touch that specific connection. Intended to be run once before version migrations
        /// so downstream migration logic can assume identities are upgraded. Identities that cannot be
        /// upgraded (e.g. no TempWeakKeyStoreKey to recover from) are left as-is and self-heal later via the
        /// lazy circle-definition reconcile path — the write-only keypair backfill is skipped for those too,
        /// since it also needs the key store key. Returns the number of identities upgraded, skipped, and
        /// keypair-provisioned so the caller can log a summary in its own trace.
        /// </summary>
        public async Task<(int upgraded, int skipped, int keyPairsProvisioned)> UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync(
            IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();

            var allIdentities = await GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            var upgraded = 0;
            var skipped = 0;
            var keyPairsProvisioned = 0;
            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var requiredKskUpgrade = identity.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade();
                if (requiredKskUpgrade)
                {
                    if (await TryUpgradeMasterKeyStoreKeyEncryptionAsync(identity, odinContext))
                    {
                        upgraded++;
                    }
                    else
                    {
                        skipped++;
                        logger.LogWarning(
                            "Identity {odinId} still requires master key encryption upgrade after attempt; leaving for lazy reconcile",
                            identity.OdinId);
                        continue;
                    }
                }

                // The KSK upgrade above writes MasterKeyEncryptedPeerKey to the db; re-fetch so we decrypt
                // the current stored value rather than the stale in-memory copy.
                var current = requiredKskUpgrade
                    ? await GetIdentityConnectionRegistrationInternalAsync(identity.OdinId)
                    : identity;

                if (current.PeerKeyStore.WriteOnlyKeyPair == null)
                {
                    var keyStoreKey = current.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
                    current.PeerKeyStore.WriteOnlyKeyPair = PeerKeyStoreWriteOnlyKey.CreateKeyPair(keyStoreKey);
                    keyStoreKey.Wipe();

                    await SaveIcrAsync(current, odinContext);
                    keyPairsProvisioned++;
                }
            }

            return (upgraded, skipped, keyPairsProvisioned);
        }

        /// <summary>
        /// Converts every pending deposited grant across all connections, for identities whose Peer Key the
        /// owner can reach.  Returns the number of connections drained and the number of grants converted.
        /// </summary>
        /// <remarks>
        /// Deposits otherwise wait on one of two events: the contact's next inbound peer request, or the
        /// owner touching that specific connection.  Neither is guaranteed to happen -- a dormant contact
        /// can hold a pending grant indefinitely -- which leaves the data in two shapes at once, and every
        /// later pass over connections then has to understand both.
        ///
        /// <para>
        /// Running it in the upgrade flow is the cheap way to make that stop: the owner is present with the
        /// master key, we are already walking every connection for the key-store-key pre-pass, and draining
        /// here means each subsequent migration sees grants in one shape.  It does mean conversion is tied
        /// to the owner running an upgrade, which is a weaker trigger than either hook -- this is a
        /// backstop, not a replacement for them.
        /// </para>
        ///
        /// <para>
        /// A connection that still requires the master-key encryption upgrade is skipped rather than
        /// failed: its Peer Key is not reachable, which is the same reason the pre-pass above skips it.
        /// Ordering matters for that reason -- run this after the pre-pass, so anything it repaired is
        /// drainable here.
        /// </para>
        /// </remarks>
        public async Task<(int connectionsDrained, int grantsConverted)> ConvertDepositedGrantsForConnectedIdentitiesAsync(
            IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var masterKey = odinContext.Caller.GetMasterKey();

            var allIdentities = await GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            var connectionsDrained = 0;
            var grantsConverted = 0;

            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!(identity.PeerKeyStore?.HasPendingDeposits ?? false))
                {
                    continue;
                }

                if (identity.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                {
                    logger.LogWarning(
                        "Identity {odinId} has pending deposits but still requires master key encryption upgrade; " +
                        "leaving them pending", identity.OdinId);
                    continue;
                }

                var keyStoreKey = identity.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
                try
                {
                    var convertedCircleIds = await ConvertDepositedGrantsAsync(identity.PeerKeyStore, keyStoreKey, odinContext);
                    if (convertedCircleIds.Count == 0)
                    {
                        continue;
                    }

                    // The master key is in hand here, unlike the peer-CAT path, so app circle grants come
                    // out with real storage keys rather than keyless.
                    await FanOutAppCircleGrantsAsync(identity, keyStoreKey, convertedCircleIds,
                        new MasterKeyStorageKeySource(masterKey), odinContext);

                    await SaveIcrAsync(identity, odinContext);

                    connectionsDrained++;
                    grantsConverted += convertedCircleIds.Count;

                    foreach (var convertedCircleId in convertedCircleIds)
                    {
                        await mediator.Publish(new ConnectionChangedNotification
                        {
                            OdinContext = odinContext,
                            OdinId = identity.OdinId,
                            CircleId = convertedCircleId,
                            Change = ConnectionChangeType.CircleGranted,
                        });
                    }
                }
                finally
                {
                    keyStoreKey.Wipe();
                }
            }

            return (connectionsDrained, grantsConverted);
        }

        private async Task<bool> UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(IdentityConnectionRegistration identity,
            IOdinContext odinContext)
        {
            if (identity.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
            {
                logger.LogDebug("Upgrading KSK Encryption for {id}", identity.OdinId);
                try
                {
                    var keyStoreKey = await publicPrivateKeyService.EccDecryptPayload(identity.TempWeakKeyStoreKey, odinContext);
                    var masterKey = odinContext.Caller.GetMasterKey();
                    var masterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, new SensitiveByteArray(keyStoreKey));
                    await circleNetworkStorage.UpdateKeyStoreKeyAsync(identity.OdinId, identity.Status, masterKeyEncryptedKeyStoreKey);
                    return true;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to upgrade KSK Encryption for {id}", identity.OdinId);
                    return false;
                }
            }

            return false;
        }
    }
}