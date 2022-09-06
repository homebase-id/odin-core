using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Creates and manages grants the access of exchanging data.
    /// </summary>
    public class ExchangeGrantService
    {
        private readonly IDriveService _driveService;
        private readonly CircleDefinitionService _circleDefinitionService;

        public ExchangeGrantService(ILogger<ExchangeGrantService> logger, IDriveService driveService, CircleDefinitionService circleDefinitionService)
        {
            _driveService = driveService;
            _circleDefinitionService = circleDefinitionService;
        }

        /// <summary>
        /// Creates an <see cref="ExchangeGrant"/> using the specified key store key
        /// </summary>
        /// <param name="grantKeyStoreKey"></param>
        /// <param name="permissionSet"></param>
        /// <param name="driveGrantRequests"></param>
        /// <param name="masterKey"></param>
        /// <returns></returns>
        public async Task<ExchangeGrant> CreateExchangeGrant(SensitiveByteArray grantKeyStoreKey, PermissionSet permissionSet, IEnumerable<DriveGrantRequest> driveGrantRequests,
            SensitiveByteArray? masterKey)
        {
            var driveGrants = new List<DriveGrant>();

            if (driveGrantRequests != null)
            {
                foreach (var req in driveGrantRequests)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var driveId = await _driveService.GetDriveIdByAlias(req.Drive, true);
                    var drive = await _driveService.GetDrive(driveId.GetValueOrDefault(), true);

                    var driveGrant = CreateDriveGrant(drive, req.Permission, grantKeyStoreKey, masterKey);
                    driveGrants.Add(driveGrant);
                }
            }

            var grant = new ExchangeGrant()
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(ref masterKey, ref grantKeyStoreKey),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = driveGrants.ToList(),
                PermissionSet = permissionSet
            };

            return grant;
        }

        /// <summary>
        /// Creates an <see cref="ExchangeGrant"/> using a generated key store key
        /// </summary>
        /// <param name="permissionSet"></param>
        /// <param name="driveGrantRequests"></param>
        /// <param name="masterKey"></param>
        /// <returns></returns>
        public async Task<ExchangeGrant> CreateExchangeGrant(PermissionSet permissionSet, IEnumerable<DriveGrantRequest> driveGrantRequests, SensitiveByteArray? masterKey)
        {
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grant = await this.CreateExchangeGrant(grantKeyStoreKey, permissionSet, driveGrantRequests, masterKey);
            grantKeyStoreKey.Wipe();

            return grant;
        }

        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grant"></param>
        /// <param name="masterKey"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(ExchangeGrant grant, SensitiveByteArray? masterKey)
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

            var token = await this.CreateClientAccessToken(grantKeyStoreKey);
            grantKeyStoreKey?.Wipe();
            return token;
        }

        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(SensitiveByteArray grantKeyStoreKey)
        {
            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantKeyStoreKey);
            return (accessReg, clientAccessToken);
        }

        public DriveGrant CreateDriveGrant(StorageDrive drive, DrivePermission permission, SensitiveByteArray grantKeyStoreKey, SensitiveByteArray masterKey)
        {
            var storageKey = masterKey == null ? null : drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

            var dk = new DriveGrant()
            {
                DriveId = drive.Id,
                Drive = drive.TargetDriveInfo,
                KeyStoreKeyEncryptedStorageKey = (storageKey == null || grantKeyStoreKey == null) ? null : new SymmetricKeyEncryptedAes(ref grantKeyStoreKey, ref storageKey),
                Permission = permission
            };

            storageKey?.Wipe();

            return dk;
        }

        public async Task<PermissionContext> CreatePermissionContext(ClientAuthenticationToken authToken, ExchangeGrant grant, AccessRegistration accessReg, bool isOwner)
        {
            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var key = authToken.AccessTokenHalfKey;
            var accessKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref key);
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);

            var grantKeyStoreKey = accessReg.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();

            //Note: we load all current anonymously-accessible drives in real time
            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                { "exchange_grant", new PermissionGroup(grant.PermissionSet, grant.KeyStoreKeyEncryptedDriveGrants, grantKeyStoreKey) },
                { "anonymous_drives", this.GetAnonymousDrivePermissionGroup() }
            };

            var permissionCtx = new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: sharedSecret,
                isOwner: isOwner
            );

            return permissionCtx;
        }

        public async Task<PermissionContext> CreatePermissionContext(ClientAuthenticationToken authToken, Dictionary<string, ExchangeGrant> grants, AccessRegistration accessReg, bool isOwner)
        {
            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var token = authToken.AccessTokenHalfKey;
            var accessKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            var grantKeyStoreKey = accessReg.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();


            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                { "anonymous_drives", this.GetAnonymousDrivePermissionGroup() }
            };

            foreach (var key in grants.Keys)
            {
                var exchangeGrant = grants[key];
                var pg = new PermissionGroup(exchangeGrant.PermissionSet, exchangeGrant.KeyStoreKeyEncryptedDriveGrants, grantKeyStoreKey);
                permissionGroupMap.Add(key, pg);
            }

            var permissionCtx = new PermissionContext(
                permissionGroupMap,
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
                Id = ByteArrayId.NewId(),
                AccessRegistrationClientType = clientType,
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ClientAccessKeyEncryptedKeyStoreKey = serverAccessKey,
                AccessKeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(ref accessKeyStoreKey, ref sharedSecret),
                IsRevoked = false,
                AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey = grantKeyStoreKey == null ? null : new SymmetricKeyEncryptedAes(secret: ref accessKeyStoreKey, dataToEncrypt: ref grantKeyStoreKey)
            };

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

        private PermissionGroup GetAnonymousDrivePermissionGroup()
        {
            var anonDriveGrants = this.GetAnonymousDriveGrants().GetAwaiter().GetResult();
            return new PermissionGroup(new PermissionSet(), anonDriveGrants, null);
        }

        private async Task<IEnumerable<DriveGrant>> GetAnonymousDriveGrants()
        {
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);
            return anonymousDrives.Results.Select(drive => this.CreateDriveGrant(drive, DrivePermission.Read, null, null));
        }
    }
}