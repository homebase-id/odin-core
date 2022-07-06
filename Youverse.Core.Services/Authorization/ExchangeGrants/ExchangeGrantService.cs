using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Creates and manages grants the access of exchanging data.
    /// </summary>
    public class ExchangeGrantService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private const string EXCHANGE_REGISTRATION = "exreg";
        private const string ACCESS_TOKEN_REG = "atr";

        public ExchangeGrantService(DotYouContextAccessor contextAccessor, ILogger<ExchangeGrantService> logger, ISystemStorage systemStorage, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
        }

        /// <summary>
        /// Creates a <see cref="YouAuthExchangeGrant"/> from the <see cref="ClientAuthenticationToken"/> of an <see cref="IdentityExchangeGrant"/>
        /// </summary>
        public async Task<(YouAuthExchangeGrant, ClientAccessToken)> SpawnYouAuthExchangeGrant(ClientAuthenticationToken icrClientAuthToken, AccessRegistrationClientType clientType)
        {
            //this will also validate the client auth token is good
            var (registration, sourceIdentityExchangeGrant) = await this.GetAccessAndIdentityGrant(icrClientAuthToken);
            if (registration.IsRevoked || sourceIdentityExchangeGrant.IsRevoked)
            {
                throw new YouverseSecurityException("Cannot create an exchange grant when the source grant is revoked");
            }

            //TODO: we can get to the grant keystore key because i have icrClientAuthToken.AccessTokenHalfKey but I cannot recreate the MasterKeyEncryptedKeyStoreKey
            // so for now I am just cloning the whole thing
            var newGrant = new YouAuthExchangeGrant()
            {
                Id = Guid.NewGuid(),
                DotYouId = sourceIdentityExchangeGrant.DotYouId,
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                MasterKeyEncryptedKeyStoreKey = sourceIdentityExchangeGrant.MasterKeyEncryptedKeyStoreKey,
                IsRevoked = sourceIdentityExchangeGrant.IsRevoked,
                KeyStoreKeyEncryptedDriveGrants = sourceIdentityExchangeGrant.KeyStoreKeyEncryptedDriveGrants,
                PermissionSet = sourceIdentityExchangeGrant.PermissionSet
            };

            _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(newGrant.ToLiteDbRecord()));

            var icrAccessReg = await this.GetAccessRegistration(icrClientAuthToken.Id);
            var sourceAccessRegistrationHalfKey = icrClientAuthToken.AccessTokenHalfKey;
            var accessKeyStoreKey = icrAccessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref sourceAccessRegistrationHalfKey);
            var grantKeyStoreKey = icrAccessReg.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey.DecryptKeyClone(ref accessKeyStoreKey);

            var (_, newClientAccessToken) = await this.CreateClientAccessTokenInternal(newGrant.Id, grantKeyStoreKey, clientType);

            accessKeyStoreKey.Wipe();
            grantKeyStoreKey.Wipe();

            return (newGrant, newClientAccessToken);
        }

        /// <summary>
        /// Creates an exchange grant for a given <see cref="DotYouIdentity"/> which can only view unencrypted data.
        /// </summary>
        /// <param name="dotYouId">The identity being granted access</param>
        /// <param name="permissionSet"></param>
        /// <param name="driveIdList"></param>
        /// <param name="clientType"></param>
        public async Task<(YouAuthExchangeGrant, ClientAccessToken)> CreateYouAuthExchangeGrant(DotYouIdentity dotYouId, PermissionSet permissionSet, List<Guid> driveIdList,
            AccessRegistrationClientType clientType)
        {
            var (grant, grantKeyStoreKey) = await this.CreateExchangeGrant<YouAuthExchangeGrant>(permissionSet, driveIdList);
            grant.DotYouId = dotYouId;

            _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(grant.ToLiteDbRecord()));

            var (_, token) = await this.CreateClientAccessTokenInternal(grant.Id, grantKeyStoreKey, clientType);
            grantKeyStoreKey.Wipe();

            return (grant, token);
        }
        

        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grantId"></param>
        /// <returns></returns>

        public async Task<ClientAccessToken> AddClientToExchangeGrant(ClientAuthenticationToken clientAuthenticationToken, AccessRegistrationClientType clientType)
        {
            var sourceAccessRegistration = await this.GetAccessRegistration(clientAuthenticationToken.Id);
            var sourceAccessRegistrationHalfKey = clientAuthenticationToken.AccessTokenHalfKey;
            var accessKeyStoreKey = sourceAccessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref sourceAccessRegistrationHalfKey);

            var grantKeyStoreKey = sourceAccessRegistration.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey.DecryptKeyClone(ref accessKeyStoreKey);
            var grant = await this.GetExchangeGrantInternal(sourceAccessRegistration.GrantId);

            var (_, newClientAccessToken) = await this.CreateClientAccessTokenInternal(grant.Id, grantKeyStoreKey, clientType);

            accessKeyStoreKey.Wipe();
            grantKeyStoreKey.Wipe();

            return newClientAccessToken;
        }


        public async Task<(SensitiveByteArray, List<DriveGrant>)> GetDrivesFromValidatedAccessRegistration(Guid accessRegId, SensitiveByteArray accessTokenHalfKey)
        {
            var accessReg = await this.GetAccessRegistration(accessRegId);
            if (accessReg is { IsRevoked: false })
            {
                var grant = await this.GetExchangeGrantInternal(accessReg.GrantId);
                if (grant is { IsRevoked: false })
                {
                    var driveGrants = grant.KeyStoreKeyEncryptedDriveGrants;
                    var keyStoreKey = accessReg.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref accessTokenHalfKey);
                    return (keyStoreKey, driveGrants);
                }
            }

            throw new YouverseSecurityException("Access Grant is Revoked");
        }

        /// <summary>
        /// Gets an exchange grant if the specified ClientAuthToken is valid.  The caller must check if
        /// the <see cref="AccessRegistration"/> and <see cref="ExchangeGrantBase"/> are revoked
        /// </summary>
        /// <param name="authenticationToken"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ExchangeGrantBase)> GetAccessAndGrant(ClientAuthenticationToken authenticationToken)
        {
            var registration = await this.GetAccessRegistration(authenticationToken.Id);

            if (null == registration)
            {
                return (null, null);
            }

            registration.AssertValidRemoteKey(authenticationToken.AccessTokenHalfKey);

            var grant = await this.GetExchangeGrantInternal(registration.GrantId);

            return (registration, grant);
        }

        public async Task<(AccessRegistration, IdentityExchangeGrant)> GetAccessAndIdentityGrant(ClientAuthenticationToken authenticationToken)
        {
            var registration = await this.GetAccessRegistration(authenticationToken.Id);

            registration.AssertValidRemoteKey(authenticationToken.AccessTokenHalfKey);

            var grant = await this.GetExchangeGrantInternal(registration.GrantId) as IdentityExchangeGrant;

            return (registration, grant);
        }

        public async Task<ExchangeGrantBase> GetExchangeGrant(Guid id)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            return await this.GetExchangeGrantInternal(id);
        }

        public async Task RevokeDrive(Guid exchangeGrantId, Guid driveId)
        {
            var grant = await this.GetExchangeGrantInternal(exchangeGrantId);
            if (null == grant)
            {
                throw new MissingDataException("Invalid Grant specified");
            }

            if ((grant.KeyStoreKeyEncryptedDriveGrants?.Count ?? 0) == 0)
            {
                return;
            }

            grant.KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants.Where(dg => dg.DriveId != driveId).ToList();

            _systemStorage.WithTenantSystemStorage<ExchangeGrantBase>(EXCHANGE_REGISTRATION, s => s.Save(grant));
        }

        public async Task GrantDrive(Guid exchangeGrantId, StorageDrive drive, DrivePermissions permissions)
        {
            var grant = await this.GetExchangeGrantInternal(exchangeGrantId);
            if (null == grant)
            {
                throw new MissingDataException("Invalid Grant specified");
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

            var grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var driveGrant = new DriveGrant()
            {
                DriveId = drive.Id,
                KeyStoreKeyEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref grantKeyStoreKey, ref storageKey),
                Permissions = permissions
            };

            storageKey.Wipe();
            grantKeyStoreKey.Wipe();

            if (grant.KeyStoreKeyEncryptedDriveGrants == null)
            {
                grant.KeyStoreKeyEncryptedDriveGrants = new List<DriveGrant>();
            }

            grant.KeyStoreKeyEncryptedDriveGrants.Add(driveGrant);

            _systemStorage.WithTenantSystemStorage<ExchangeGrantBase>(EXCHANGE_REGISTRATION, s => s.Save(grant));
        }

        public async Task<AccessRegistration> GetAccessRegistration(Guid id)
        {
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Get(id));
            return eg;
        }

        
        /////

        private async Task<ExchangeGrantBase> GetExchangeGrantInternal(Guid id)
        {
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Get(id));

            if (null == eg)
            {
                return null;
            }

            if (eg.StorageType == ExchangeGranteeType.YouAuth)
            {
                return YouAuthExchangeGrant.FromLiteDbRecord(eg);
            }

            throw new MissingDataException("Invalid Storage type on ExchangeGrantLiteDbRecord");
        }

        /// <summary>
        /// Creates a new <see cref="ClientAuthenticationToken"/> from an existing <see cref="ExchangeGrantBase"/> which can be given to remote callers for access to
        /// data.  If the <param name="grantKeyStoreKey">grantKeyStoreKey</param> is null, the AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey will
        /// be null
        /// </summary>
        /// <returns></returns>
        private Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessTokenInternal(Guid grantId, SensitiveByteArray grantKeyStoreKey,
            AccessRegistrationClientType clientType = AccessRegistrationClientType.Other)
        {
            var accessKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var serverAccessKey = new SymmetricKeyEncryptedXor(ref accessKeyStoreKey, out var clientAccessKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new AccessRegistration()
            {
                Id = Guid.NewGuid(),
                AccessRegistrationClientType = clientType,
                GrantId = grantId,
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

            _systemStorage.WithTenantSystemStorage<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Save(reg));

            return Task.FromResult((reg, cat));
        }

        private async Task<(T, SensitiveByteArray)> CreateExchangeGrant<T>(PermissionSet permissionSet, List<Guid> driveIdList, SensitiveByteArray masterKey = null)
            where T : ExchangeGrantBase, new()
        {
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grants = new List<DriveGrant>();

            List<StorageDrive> drives = new List<StorageDrive>();

            if (driveIdList != null)
            {
                foreach (var id in driveIdList)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var drive = await _driveService.GetDrive(id, true);
                    drives.Add(drive);
                }
            }

            //TODO: need to handle scenario when a new anon drive is added.  all grants must be updated
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);
            drives.AddRange(anonymousDrives.Results);

            foreach (var drive in drives)
            {
                //ignore duplicates 
                if (grants.Any(x => x.DriveAlias == drive.Alias))
                {
                    continue;
                }

                var storageKey = masterKey == null ? null : drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new DriveGrant()
                {
                    DriveId = drive.Id,
                    DriveAlias = drive.Alias,
                    DriveType = drive.Type,
                    KeyStoreKeyEncryptedStorageKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(ref grantKeyStoreKey, ref storageKey),
                    Permissions = DrivePermissions.Read //Hard coded until we support writing data into the system
                };

                storageKey?.Wipe();
                grants.Add(dk);
            }

            var grant = new T()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(ref masterKey, ref grantKeyStoreKey),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = grants,
                PermissionSet = permissionSet
            };

            return (grant, grantKeyStoreKey);
        }
    }
}