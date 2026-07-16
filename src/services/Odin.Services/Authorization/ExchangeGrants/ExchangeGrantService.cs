#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Creates and manages grants the access of exchanging data.
    /// </summary>
    public class ExchangeGrantService
    {
        private readonly ILogger<ExchangeGrantService> _logger;
        private readonly IDriveManager _driveManager;

        public ExchangeGrantService(ILogger<ExchangeGrantService> logger, IDriveManager driveManager)
        {
            _logger = logger;
            _driveManager = driveManager;
        }

        /// <summary>
        /// Creates an <see cref="KeyStore"/> using the specified key store key. Drive storage
        /// keys come from <paramref name="storageKeySource"/>; the master key is used only to
        /// wrap the key store key for the owner.
        /// </summary>
        public async Task<KeyStore> CreateExchangeGrantAsync(
            SensitiveByteArray keyStoreKey,
            PermissionSet permissionSet,
            IEnumerable<DriveGrantRequest>? driveGrantRequests,
            IStorageKeySource storageKeySource,
            SensitiveByteArray? masterKey,
            SensitiveByteArray? icrKey = null)
        {
            var driveGrants = new List<DriveGrant>();

            if (driveGrantRequests != null)
            {
                foreach (var req in driveGrantRequests)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var driveId = req.PermissionedDrive.Drive.Alias;
                    var drive = await _driveManager.GetDriveAsync(driveId, true);

                    var driveGrant = CreateDriveGrant(drive, req.PermissionedDrive.Permission, keyStoreKey, storageKeySource,
                        req.PermissionedDrive.TemporalReadWindowSeconds);
                    driveGrants.Add(driveGrant);
                }
            }

            var grant = new KeyStore()
            {
                Created = UnixTimeUtc.Now().milliseconds,
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                DriveGrants = driveGrants.ToList(),
                KeyStoreKeyEncryptedIcrKey = icrKey == null ? null : new SymmetricKeyEncryptedAes(keyStoreKey, icrKey),
                PermissionSet = permissionSet
            };

            return grant;
        }

        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grant"></param>
        /// <param name="masterKey"></param>
        /// <param name="tokenType"></param>
        /// <returns></returns>
        public async Task<(ServerHalfOfClientKey, ClientAccessToken)> CreateClientAccessToken(KeyStore grant,
            SensitiveByteArray? masterKey,
            ClientTokenType tokenType)
        {
            if (grant.IsRevoked)
            {
                throw new OdinSecurityException("Cannot create Client Access Token for a revoked ExchangeGrant");
            }

            SensitiveByteArray? grantKeyStoreKey = null;

            if (masterKey != null)
            {
                grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            }

            // grant.KeyStoreKeyEncryptedIcrKey

            var token = await this.CreateClientAccessToken(grantKeyStoreKey, tokenType);
            grantKeyStoreKey?.Wipe();
            return token;
        }

        public async Task<(ServerHalfOfClientKey, ClientAccessToken)> CreateClientAccessToken(SensitiveByteArray? keyStoreKey,
            ClientTokenType tokenType,
            SensitiveByteArray? sharedSecret = null)
        {
            var (accessReg, clientAccessToken) =
                await this.CreateClientAccessTokenInternal(keyStoreKey, tokenType, sharedSecret: sharedSecret);
            return (accessReg, clientAccessToken);
        }

        public async Task<PermissionContext> CreatePermissionContext(
            ClientAuthenticationToken authToken,
            Dictionary<Guid, KeyStore>? grants,
            ServerHalfOfClientKey accessReg,
            IOdinContext odinContext,
            List<int>? additionalPermissionKeys = null,
            bool includeAnonymousDrives = false,
            DrivePermission anonymousDrivePermission = DrivePermission.Read)
        {
            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var (grantKeyStoreKey, sharedSecret) = accessReg.DecryptUsingClientAuthenticationToken(authToken);

            var permissionGroupMap = new Dictionary<string, PermissionGroup>();
            if (grants != null)
            {
                foreach (var key in grants.Keys)
                {
                    var exchangeGrant = grants[key];

                    var pg = new PermissionGroup(
                        exchangeGrant.PermissionSet,
                        exchangeGrant.DriveGrants,
                        grantKeyStoreKey,
                        exchangeGrant.KeyStoreKeyEncryptedIcrKey);
                    permissionGroupMap.Add(key.ToString(), pg);
                }
            }

            if (includeAnonymousDrives)
            {
                //TODO: remove any anonymous drives which are explicitly granted above
                var anonPg = await this.CreateAnonymousDrivePermissionGroup(anonymousDrivePermission, odinContext);
                permissionGroupMap.Add("anonymous_drives", anonPg);
            }

            if (additionalPermissionKeys != null)
            {
                permissionGroupMap.Add("additional_permissions",
                    new PermissionGroup(new PermissionSet(additionalPermissionKeys), null, null, null));
            }

            var grantedKeys = (grants?.Values.SelectMany(g => g.PermissionSet?.Keys ?? []) ?? [])
                .Concat(additionalPermissionKeys ?? []);
            var impliedKeys = PermissionKeyImplications.ResolveImpliedKeys(grantedKeys);
            if (impliedKeys.Count > 0)
            {
                permissionGroupMap.Add("implied_permissions",
                    new PermissionGroup(new PermissionSet(impliedKeys), null, null, null));
            }

            var permissionCtx = new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: sharedSecret,
                keyStoreKey: grantKeyStoreKey
            );

            return permissionCtx;
        }


        /// <summary>
        /// Creates a permission group of anonymous drives
        /// </summary>
        private async Task<PermissionGroup> CreateAnonymousDrivePermissionGroup(DrivePermission permissions, IOdinContext odinContext)
        {
            var anonymousDrives = await _driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);
            var anonDriveGrants = anonymousDrives.Results.Select(drive =>
                this.CreateDriveGrant(drive, permissions, null, NoStorageKeySource.Instance));
            return new PermissionGroup(new PermissionSet(), anonDriveGrants, null, null);
        }

        //

        private DriveGrant CreateDriveGrant(StorageDrive drive, DrivePermission permission, SensitiveByteArray? grantKeyStoreKey,
            IStorageKeySource storageKeySource, long? temporalReadWindowSeconds = null)
        {
            // ConditionalTemporalRead also needs the storage key escrowed so the grantee can decrypt
            // in-window files while the owner is offline; the temporal API enforces the time clamp.
            bool shouldGetStorageKey = permission.HasFlag(DrivePermission.Read) ||
                                       permission.HasFlag(DrivePermission.ConditionalTemporalRead);

            var storageKey = shouldGetStorageKey ? storageKeySource.GetStorageKey(drive) : null;

            SymmetricKeyEncryptedAes? keyStoreKeyEncryptedStorageKey = null;
            if (storageKey != null && grantKeyStoreKey != null)
            {
                keyStoreKeyEncryptedStorageKey = new SymmetricKeyEncryptedAes(grantKeyStoreKey, storageKey);
            }
            else if (shouldGetStorageKey && grantKeyStoreKey != null)
            {
                _logger.LogDebug("Minting read drive grant for {drive} without a storage key (keyless source); " +
                                 "the member cannot decrypt until the grant is re-minted", drive.TargetDriveInfo);
            }

            var dk = new DriveGrant()
            {
                DriveId = drive.Id,
                KeyStoreKeyEncryptedStorageKey = keyStoreKeyEncryptedStorageKey,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = drive.TargetDriveInfo,
                    Permission = permission,
                    TemporalReadWindowSeconds = temporalReadWindowSeconds
                }
            };

            storageKey?.Wipe();

            return dk;
        }

        private Task<(ServerHalfOfClientKey, ClientAccessToken)> CreateClientAccessTokenInternal(
            SensitiveByteArray? keyStoreKey,
            ClientTokenType tokenType,
            AccessRegistrationClientType clientType = AccessRegistrationClientType.Other, SensitiveByteArray? sharedSecret = null)
        {
            var clientKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            
            // now split the client key, give one to the server, and one to the client
            var serverHalfOfKey = new SymmetricKeyEncryptedXor(clientKey, out var clientHalfOfKey);

            var ss = sharedSecret ?? ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new ServerHalfOfClientKey()
            {
                Id = SequentialGuid.CreateGuid(),
                
                AccessRegistrationClientType = clientType,
                
                Created = UnixTimeUtc.Now().milliseconds,
                
                ServerHalfOfKey = serverHalfOfKey,
                
                ClientKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(secret: clientKey, dataToEncrypt: ss),
              
                IsRevoked = false,
                
                ClientKeyEncryptedKeyStoreKey = keyStoreKey == null
                    ? null
                    : new SymmetricKeyEncryptedAes(secret: clientKey, dataToEncrypt: keyStoreKey)
            };

            clientKey.Wipe();

            //Note: we have to send both the id and the AccessTokenHalfKey back to the server
            var cat = new ClientAccessToken()
            {
                Id = reg.Id,
                AccessTokenHalfKey = clientHalfOfKey,
                SharedSecret = ss,
                ClientTokenType = tokenType
            };

            return Task.FromResult((reg, cat));
        }
    }
}