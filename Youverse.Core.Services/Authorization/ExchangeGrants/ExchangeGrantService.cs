using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Creates and manages grants the access of exchanging data.
    /// </summary>
    public class ExchangeGrantService
    {
        private readonly IDriveService _driveService;

        public ExchangeGrantService(ILogger<ExchangeGrantService> logger, IDriveService driveService)
        {
            _driveService = driveService;
        }

        public async Task<IExchangeGrant> CreateExchangeGrant(PermissionSet permissionSet, IEnumerable<DriveGrantRequest> driveGrantRequests, SensitiveByteArray? masterKey)
        {
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grants = new List<DriveGrant>();

            if (driveGrantRequests != null)
            {
                foreach (var req in driveGrantRequests)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var driveId = await _driveService.GetDriveIdByAlias(req.Drive, true);
                    var drive = await _driveService.GetDrive(driveId.GetValueOrDefault(), true);

                    var driveGrant = CreateDriveGrant(drive, req.Permission, grantKeyStoreKey, masterKey);
                    grants.Add(driveGrant);
                }
            }
            
            var grant = new ExchangeGrant()
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(ref masterKey, ref grantKeyStoreKey),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = grants.ToList(),
                PermissionSet = permissionSet
            };

            grantKeyStoreKey.Wipe();

            return grant;
        }

        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grant"></param>
        /// <param name="masterKey"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(IExchangeGrant grant, SensitiveByteArray? masterKey)
        {
            if (grant.IsRevoked)
            {
                throw new YouverseSecurityException("Cannot create Client Access Token for a revoked ExchangeGrant");
            }

            SensitiveByteArray grantKeyStoreKey = null;

            if (masterKey != null)
            {
                grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            }

            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantKeyStoreKey);
            grantKeyStoreKey?.Wipe();

            return (accessReg, clientAccessToken);
        }
        
        public DriveGrant CreateDriveGrant(StorageDrive drive, DrivePermission permission, SensitiveByteArray grantKeyStoreKey, SensitiveByteArray masterKey)
        {
            var storageKey = masterKey == null ? null : drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

            var dk = new DriveGrant()
            {
                DriveId = drive.Id,
                DriveAlias = drive.Alias,
                DriveType = drive.Type,
                KeyStoreKeyEncryptedStorageKey = (storageKey == null || grantKeyStoreKey == null) ? null : new SymmetricKeyEncryptedAes(ref grantKeyStoreKey, ref storageKey),
                Permission = permission
            };

            storageKey?.Wipe();

            return dk;
        }

        public async Task RevokeDrive(IExchangeGrant grant, Guid driveId)
        {
            throw new NotImplementedException("");
        }

        public async Task GrantDrive(IExchangeGrant grant, StorageDrive drive, DrivePermission permission)
        {
            throw new NotImplementedException("");
        }


        public async Task<PermissionContext> CreatePermissionContext(ClientAuthenticationToken authToken, IExchangeGrant grant, AccessRegistration accessReg, bool isOwner)
        {
            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var key = authToken.AccessTokenHalfKey;
            var accessKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref key);
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);

            var grantKeyStoreKey = accessReg.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();

            //Note: we load all current anonymously-accessible drives in real time
            var mergedDriveGrants = this.MergeAnonymousDrives(grant.KeyStoreKeyEncryptedDriveGrants).GetAwaiter().GetResult();

            var permissionCtx = new PermissionContext(
                driveGrants: mergedDriveGrants,
                permissionSet: grant.PermissionSet,
                driveDecryptionKey: grantKeyStoreKey,
                sharedSecretKey: sharedSecret,
                isOwner: isOwner
            );

            return permissionCtx;
        }

        //

        private Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessTokenInternal(SensitiveByteArray grantKeyStoreKey,
            AccessRegistrationClientType clientType = AccessRegistrationClientType.Other)
        {
            var accessKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var serverAccessKey = new SymmetricKeyEncryptedXor(ref accessKeyStoreKey, out var clientAccessKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new AccessRegistration()
            {
                Id = Guid.NewGuid(),
                AccessRegistrationClientType = clientType,
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ClientAccessKeyEncryptedKeyStoreKey = serverAccessKey,
                AccessKeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(ref accessKeyStoreKey, ref sharedSecret),
                IsRevoked = false,
                AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey = grantKeyStoreKey == null ? null : new SymmetricKeyEncryptedAes(secret: ref accessKeyStoreKey, dataToEncrypt: ref grantKeyStoreKey)
            };

            grantKeyStoreKey?.Wipe();
            accessKeyStoreKey.Wipe();

            //Note: we have to send both the id and the AccessTokenHalfKey back to the server
            var cat = new ClientAccessToken()
            {
                Id = reg.Id,
                AccessTokenHalfKey = clientAccessKey,
                SharedSecret = sharedSecret
            };

            return Task.FromResult((reg, cat));
        }

        private async Task<IEnumerable<DriveGrant>> MergeAnonymousDrives(IEnumerable<DriveGrant> driveGrants)
        {
            var list = driveGrants?.ToList();
            Guard.Argument(list, nameof(driveGrants)).NotNull();

            //get the anonymous drives
            //merge; existing drive grants take priority because they likely have keys for decryption
            var finalGrantList = new List<DriveGrant>(list!);
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);
            
            foreach (var drive in anonymousDrives.Results)
            {
                //add new ones
                if (list.All(g => g.DriveId != drive.Id))
                {
                    var grant = CreateDriveGrant(drive, DrivePermission.Read, null, null);
                    finalGrantList.Add(grant);
                }
            }

            return finalGrantList;
        }
    }
}