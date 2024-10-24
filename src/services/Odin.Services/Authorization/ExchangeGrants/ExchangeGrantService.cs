#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite.IdentityDatabase;
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
        private readonly DriveManager _driveManager;

        public ExchangeGrantService(ILogger<ExchangeGrantService> logger, DriveManager driveManager)
        {
            _logger = logger;
            _driveManager = driveManager;
        }

        /// <summary>
        /// Creates an <see cref="ExchangeGrant"/> using the specified key store key
        /// </summary>
        public async Task<ExchangeGrant> CreateExchangeGrant(
            IdentityDatabase db,
            SensitiveByteArray grantKeyStoreKey, PermissionSet permissionSet,
            IEnumerable<DriveGrantRequest>? driveGrantRequests,
            SensitiveByteArray? masterKey,
            SensitiveByteArray? icrKey = null)
        {
            var driveGrants = new List<DriveGrant>();

            if (driveGrantRequests != null)
            {
                foreach (var req in driveGrantRequests)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var driveId = await _driveManager.GetDriveIdByAlias(req.PermissionedDrive.Drive, db, true);
                    var drive = await _driveManager.GetDrive(driveId.GetValueOrDefault(), db, true);

                    var driveGrant = CreateDriveGrant(drive, req.PermissionedDrive.Permission, grantKeyStoreKey, masterKey);
                    driveGrants.Add(driveGrant);
                }
            }

            var grant = new ExchangeGrant()
            {
                Created = UnixTimeUtc.Now().milliseconds,
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(masterKey, grantKeyStoreKey),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = driveGrants.ToList(),
                KeyStoreKeyEncryptedIcrKey = icrKey == null ? null : new SymmetricKeyEncryptedAes(grantKeyStoreKey, icrKey),
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
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(ExchangeGrant grant, SensitiveByteArray? masterKey,
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

        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(SensitiveByteArray? grantKeyStoreKey, ClientTokenType tokenType,
            SensitiveByteArray? sharedSecret = null)
        {
            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantKeyStoreKey, tokenType, sharedSecret: sharedSecret);
            return (accessReg, clientAccessToken);
        }

        public async Task<PermissionContext> CreatePermissionContext(
            ClientAuthenticationToken authToken,
            Dictionary<Guid, ExchangeGrant>? grants,
            AccessRegistration accessReg,
            IOdinContext odinContext,
            IdentityDatabase db,
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

                    var pg = new PermissionGroup(exchangeGrant.PermissionSet, exchangeGrant.KeyStoreKeyEncryptedDriveGrants, grantKeyStoreKey,
                        exchangeGrant.KeyStoreKeyEncryptedIcrKey);
                    permissionGroupMap.Add(key.ToString(), pg);

                    foreach (var x in exchangeGrant.KeyStoreKeyEncryptedDriveGrants)
                    {
                        _logger.LogTrace(
                            $"Auth Token with Id: [{authToken.Id}] Access granted to drive [{x.DriveId}] (alias:{x.PermissionedDrive.Drive.Alias} | type: {x.PermissionedDrive.Drive.Type})");
                    }
                }
            }

            if (includeAnonymousDrives)
            {
                //TODO: remove any anonymous drives which are explicitly granted above
                var anonPg = await this.CreateAnonymousDrivePermissionGroup(anonymousDrivePermission, odinContext, db);
                permissionGroupMap.Add("anonymous_drives", anonPg);
            }

            if (additionalPermissionKeys != null)
            {
                permissionGroupMap.Add("additional_permissions", new PermissionGroup(new PermissionSet(additionalPermissionKeys), null, null, null));
            }

            var permissionCtx = new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: sharedSecret
            );

            return permissionCtx;
        }


        /// <summary>
        /// Creates a permission group of anonymous drives
        /// </summary>
        private async Task<PermissionGroup> CreateAnonymousDrivePermissionGroup(DrivePermission permissions, IOdinContext odinContext, IdentityDatabase db)
        {
            var anonymousDrives = await _driveManager.GetAnonymousDrives(PageOptions.All, odinContext, db);
            var anonDriveGrants = anonymousDrives.Results.Select(drive => this.CreateDriveGrant(drive, permissions, null, null));
            return new PermissionGroup(new PermissionSet(), anonDriveGrants, null, null);
        }

        //

        private DriveGrant CreateDriveGrant(StorageDrive drive, DrivePermission permission, SensitiveByteArray? grantKeyStoreKey, SensitiveByteArray? masterKey)
        {
            var storageKey = masterKey == null ? null : drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey);

            SymmetricKeyEncryptedAes? keyStoreKeyEncryptedStorageKey = null;

            bool shouldGetStorageKey = permission.HasFlag(DrivePermission.Read);
            if (shouldGetStorageKey && storageKey != null && grantKeyStoreKey != null)
            {
                keyStoreKeyEncryptedStorageKey = new SymmetricKeyEncryptedAes(grantKeyStoreKey, storageKey);
            }

            var dk = new DriveGrant()
            {
                DriveId = drive.Id,
                KeyStoreKeyEncryptedStorageKey = keyStoreKeyEncryptedStorageKey,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = drive.TargetDriveInfo,
                    Permission = permission
                }
            };

            storageKey?.Wipe();

            return dk;
        }

        private Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessTokenInternal(SensitiveByteArray? grantKeyStoreKey, ClientTokenType tokenType,
            AccessRegistrationClientType clientType = AccessRegistrationClientType.Other, SensitiveByteArray? sharedSecret = null)
        {
            var accessKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var serverAccessKey = new SymmetricKeyEncryptedXor(accessKeyStoreKey, out var clientAccessKey);

            var ss = sharedSecret ?? ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new AccessRegistration()
            {
                Id = SequentialGuid.CreateGuid(),
                AccessRegistrationClientType = clientType,
                Created = UnixTimeUtc.Now().milliseconds,
                ClientAccessKeyEncryptedKeyStoreKey = serverAccessKey,
                AccessKeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(secret: accessKeyStoreKey, dataToEncrypt: ss),
                IsRevoked = false,
                AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey = grantKeyStoreKey == null
                    ? null
                    : new SymmetricKeyEncryptedAes(secret: accessKeyStoreKey, dataToEncrypt: grantKeyStoreKey)
            };

            accessKeyStoreKey.Wipe();

            //Note: we have to send both the id and the AccessTokenHalfKey back to the server
            var cat = new ClientAccessToken()
            {
                Id = reg.Id,
                AccessTokenHalfKey = clientAccessKey,
                SharedSecret = ss,
                ClientTokenType = tokenType
            };

            return Task.FromResult((reg, cat));
        }
    }
}