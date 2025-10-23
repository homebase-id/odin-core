using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.Caller;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;
using Odin.Services.Registry;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Tenant;

// Goals here are that:
//   * the password never leaves the clients.
//   * the password hash changes with every login request, making playback impossible
//   * the private encryption key on the server is encrypted with a KEK
//   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
//   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
namespace Odin.Services.Authentication.Owner
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService(
        ILogger<OwnerAuthenticationService> logger,
        OwnerSecretService secretService,
        TenantContext tenantContext,
        IDriveManager driveManager,
        IcrKeyService icrKeyService,
        TenantConfigService tenantConfigService,
        IIdentityRegistry identityRegistry,
        OdinConfiguration configuration,
        TableKeyValueCached tblKeyValue,
        ShamirRecoveryService shamirRecoveryService,
        OdinContextCache cache,
        ICallerLogContext callerLogContext,
        ITenantProvider tenantProvider,
        ClientRegistrationStorage clientRegistrationStorage)
        : INotificationHandler<DriveDefinitionAddedNotification>
    {
        private const string FirstRunContextKey = "c05d8c71-e75f-4998-ad74-7e94d8752b56";

        private static readonly SingleKeyValueStorage FirstRunInfoStorage =
            TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(FirstRunContextKey));

        /// <summary>
        /// Authenticates the owner based on the <see cref="PasswordReply"/> specified.
        /// </summary>
        /// <param name="reply"></param>
        /// <param name="devicePushNotificationKey"></param>
        /// <param name="odinContext"></param>
        /// <exception cref="OdinSecurityException">Thrown when a user cannot be authenticated</exception>
        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> AuthenticateAsync(PasswordReply reply,
            Guid devicePushNotificationKey, IOdinContext odinContext)
        {
            var noncePackage = await secretService.AssertValidPasswordAsync(reply);

            //now that the password key matches, we set return the client auth token
            var keys = await secretService.GetOfflineEccKeyListAsync();

            var issuedTo = (OdinId)tenantProvider.GetCurrentTenant()?.Name;
            var (clientToken, serverToken) = OwnerConsoleTokenManager.CreateToken(issuedTo, noncePackage, reply, keys);
            await clientRegistrationStorage.SaveAsync(serverToken);

            // TODO: audit login some where, or in helper class below

            var token = new ClientAuthenticationToken()
            {
                Id = serverToken.Id,
                AccessTokenHalfKey = new SensitiveByteArray(clientToken.GetKey()),
                ClientTokenType = ClientTokenType.Other
            };

            var clientContext = new OdinClientContext()
            {
                ClientIdOrDomain = string.Empty,
                CorsHostName = string.Empty,
                AccessRegistrationId = token.Id,
                DevicePushNotificationKey = devicePushNotificationKey
            };

            //set the odin context so the request of this request can use the master key (note: this was added, so we could set keys on first login)
            await this.UpdateOdinContextAsync(token, clientContext, odinContext);
            await EnsureFirstRunOperationsAsync(odinContext);

            //if You've successfully logged in, you can't be recovery mode
            await shamirRecoveryService.ForceExitRecoveryMode(odinContext);

            return (token, serverToken.SharedSecret.ToSensitiveByteArray());
        }

        /// <summary>
        /// Determines if the <paramref name="sessionTokenId"/> is valid and has not expired.  
        /// </summary>
        /// <param name="sessionTokenId">The token to be validated</param>
        /// <returns></returns>
        public async Task<bool> IsValidTokenAsync(Guid sessionTokenId)
        {
            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            var entry = await clientRegistrationStorage.GetAsync<OwnerConsoleClientRegistration>(sessionTokenId);
            return IsAuthTokenEntryValid(entry);
        }

        /// <summary>
        /// Returns the LoginKek used to access the primary and application data encryption keys
        /// </summary>
        public async Task<(SensitiveByteArray, SensitiveByteArray)> GetMasterKeyAsync(Guid sessionTokenId, SensitiveByteArray clientSecret)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = await clientRegistrationStorage.GetAsync<OwnerConsoleClientRegistration>(sessionTokenId);

            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new OdinClientException("Token is invalid", OdinClientErrorCode.InvalidAuthToken);
            }

            var mk = await secretService.GetMasterKeyAsync(loginToken, clientSecret);

            //HACK: need to clone this here because the owner console token is getting wipe by the owner console token finalizer
            var len = loginToken!.SharedSecret.Length;
            var clone = new byte[len];
            Buffer.BlockCopy(loginToken.SharedSecret, 0, clone, 0, len);

            loginToken.Dispose();
            return (mk, clone.ToSensitiveByteArray());
        }

        public async Task<(SensitiveByteArray masterKey, PermissionContext permissionContext)> GetPermissionContextAsync(
            ClientAuthenticationToken token,
            IOdinContext odinContext)
        {
            if (await IsValidTokenAsync(token.Id))
            {
                var (masterKey, clientSharedSecret) = await GetMasterKeyAsync(token.Id, token.AccessTokenHalfKey);

                var icrKey = await icrKeyService.GetMasterKeyEncryptedIcrKeyAsync();

                var allDrives = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);
                var allDriveGrants = allDrives.Results.Select(d => new DriveGrant()
                {
                    DriveId = d.Id,
                    KeyStoreKeyEncryptedStorageKey = d.MasterKeyEncryptedStorageKey,
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = d.TargetDriveInfo,
                        Permission = DrivePermission.All
                    },
                });

                var permissionGroupMap = new Dictionary<string, PermissionGroup>
                {
                    { "owner_grants", new PermissionGroup(new PermissionSet(PermissionKeys.All), allDriveGrants, masterKey, icrKey) },
                };

                var ctx = new PermissionContext(permissionGroupMap, clientSharedSecret);

                return (masterKey, ctx);
            }

            throw new OdinSecurityException("Invalid owner token");
        }

        /// <summary>
        /// Gets the <see cref="OdinContext"/> for the specified token from cache or disk.
        /// </summary>
        public async Task<IOdinContext> GetDotYouContextAsync(ClientAuthenticationToken token, OdinClientContext clientContext,
            IOdinContext odinContext)
        {
            var creator = new Func<Task<IOdinContext>>(async () =>
            {
                var dotYouContext = new OdinContext();
                var (masterKey, permissionContext) = await GetPermissionContextAsync(token, odinContext);

                if (null == permissionContext || masterKey.IsEmpty())
                {
                    throw new OdinSecurityException("Invalid owner token");
                }

                dotYouContext.SetPermissionContext(permissionContext);

                dotYouContext.Caller = new CallerContext(
                    odinId: tenantContext.HostOdinId,
                    masterKey: masterKey,
                    securityLevel: SecurityGroupType.Owner,
                    odinClientContext: clientContext);

                return dotYouContext;
            });

            return await cache.GetOrAddContextAsync(token, creator);
        }

        /// <summary>
        /// Expires the <paramref name="tokenId"/> thus making it invalid.  This can be used when a user
        /// clicks logout.  Invalid or expired tokens are ignored.
        /// </summary>
        /// <param name="tokenId"></param>
        public async Task ExpireTokenAsync(Guid tokenId)
        {
            await clientRegistrationStorage.DeleteAsync(tokenId);
        }

        private async Task<OwnerConsoleClientRegistration> GetValidatedEntryAsync(Guid tokenId)
        {
            var entry = await clientRegistrationStorage.GetAsync<OwnerConsoleClientRegistration>(tokenId);
            AssertTokenIsValid(entry);
            return entry;
        }

        private bool IsAuthTokenEntryValid(OwnerConsoleClientRegistration entry)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var valid = null != entry &&
                        entry.Id != Guid.Empty &&
                        entry.ExpiryUnixTime > now;

            return valid;
        }

        private void AssertTokenIsValid(OwnerConsoleClientRegistration entry)
        {
            if (IsAuthTokenEntryValid(entry) == false)
            {
                throw new OdinSecurityException("Auth token entry is invalid");
            }
        }

        public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            //reset cache so the drive is reached on the next request
            if (notification.IsNewDrive)
            {
                logger.LogDebug("New drive created [{0}]; Purging cache ", notification.Drive.TargetDriveInfo);
                await cache.ResetAsync();
            }
        }

        public async Task<bool> UpdateOdinContextAsync(ClientAuthenticationToken token, OdinClientContext clientContext,
            IOdinContext odinContext)
        {
            odinContext.SetAuthContext(OwnerAuthConstants.SchemeName);

            //HACK: fix this
            //a bit of a hack here: we have to set the context as owner
            //because it's required to build the permission context
            // this is justified because we're heading down the owner api path
            // just below this, we check to see if the token was good.  if not, the call fails.
            odinContext.Caller = new CallerContext(
                odinId: tenantContext.HostOdinId,
                masterKey: null, //will be set later
                securityLevel: SecurityGroupType.Owner,
                odinClientContext: clientContext ?? new OdinClientContext
                {
                    CorsHostName = null,
                    AccessRegistrationId = null,
                    DevicePushNotificationKey = null,
                    ClientIdOrDomain = null
                });

            IOdinContext ctx = await this.GetDotYouContextAsync(token, clientContext, odinContext);

            if (null == ctx)
            {
                return false;
            }

            //🐈⏰
            var catTime = SequentialGuid.ToUnixTimeUtc(token.Id);
            odinContext.AuthTokenCreated = catTime;

            odinContext.Caller = ctx.Caller;
            callerLogContext.Caller = odinContext.Caller.OdinId;
            odinContext.SetPermissionContext(ctx.PermissionsContext);

            return true;
        }

        //

        private async Task EnsureFirstRunOperationsAsync(IOdinContext odinContext)
        {
            var fli = await FirstRunInfoStorage.GetAsync<FirstOwnerLoginInfo>(tblKeyValue, FirstOwnerLoginInfo.Key);
            if (fli == null)
            {
                await tenantConfigService.CreateInitialKeysAsync(odinContext);

                await FirstRunInfoStorage.UpsertAsync(tblKeyValue, FirstOwnerLoginInfo.Key, new FirstOwnerLoginInfo()
                {
                    FirstLoginDate = UnixTimeUtc.Now()
                });

                // put new identity on latest version of data from the get go so upgrades dont run                
                await tenantConfigService.ForceVersionNumberAsync(Version.DataVersionNumber);
            }
        }

        public async Task MarkForDeletionAsync(PasswordReply currentPasswordReply, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var _ = await secretService.AssertValidPasswordAsync(currentPasswordReply);
            await identityRegistry.MarkForDeletionAsync(tenantContext.HostOdinId);

            var tc = identityRegistry.CreateTenantContext(tenantContext.HostOdinId);
            tc.Update(tc);
        }

        public async Task UnmarkForDeletionAsync(PasswordReply currentPasswordReply, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var _ = await secretService.AssertValidPasswordAsync(currentPasswordReply);
            await identityRegistry.UnmarkForDeletionAsync(tenantContext.HostOdinId);

            var tc = identityRegistry.CreateTenantContext(tenantContext.HostOdinId);
            tc.Update(tc);
        }

        public async Task<AccountStatusResponse> GetAccountStatusAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var idReg = await identityRegistry.GetAsync(tenantContext.HostOdinId);

            return new AccountStatusResponse()
            {
                PlannedDeletionDate = idReg.MarkedForDeletionDate.HasValue
                    ? idReg.MarkedForDeletionDate.Value.AddDays(configuration.Registry.DaysUntilAccountDeletion)
                    : null,
                PlanId = idReg.PlanId,
            };
        }

        public async Task ExtendTokenLife(Guid id)
        {
            await clientRegistrationStorage.ExtendLife(id);
        }
    }
}