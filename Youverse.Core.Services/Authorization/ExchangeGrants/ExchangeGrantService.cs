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
        /// Creates an exchange grant for a given <see cref="DotYouIdentity"/>.
        /// </summary>
        /// <param name="dotYouId">The identity being granted access</param>
        /// <param name="permissionSet"></param>
        /// <param name="driveIdList"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> RegisterIdentityExchangeGrant(DotYouIdentity dotYouId, PermissionSet permissionSet, List<Guid> driveIdList)
        {
            //check to see if we have a grant with this identity already, if that grant is only for unencrypted data, upgrade it

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var (grant, grantKeyStoreKey) = await this.CreateExchangeGrant<IdentityExchangeGrant>(permissionSet, driveIdList, masterKey);
            grant.DotYouId = dotYouId;

            var record = grant.ToLiteDbRecord();
            _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(record));

            var (accessRegistration, token) = await this.CreateClientAccessTokenInternal(grant.Id, grantKeyStoreKey);
            grantKeyStoreKey.Wipe();

            return (accessRegistration, token);
        }

        public async Task<(AccessRegistration, ClientAccessToken)> RegisterAppExchangeGrant(Guid appId, PermissionSet permissionSet, List<Guid> driveIdList)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var (grant, grantKeyStoreKey) = await this.CreateExchangeGrant<AppExchangeGrant>(permissionSet, driveIdList, masterKey);
            grant.AppId = appId;

            var record = grant.ToLiteDbRecord();
            _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(record));

            var (accessRegistration, token) = await this.CreateClientAccessTokenInternal(grant.Id, grantKeyStoreKey);
            grantKeyStoreKey.Wipe();

            return (accessRegistration, token);
        }

        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grantId"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(Guid grantId)
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();
            var grant = await this.GetExchangeGrantInternal(grantId);

            if (grant.IsRevoked)
            {
                throw new YouverseSecurityException("Cannot create Client Access Token for a revoked ExchangeGrant");
            }

            var mk = context.Caller.GetMasterKey();
            var grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref mk);

            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantId, grantKeyStoreKey);
            grantKeyStoreKey.Wipe();
            return (accessReg, clientAccessToken);
        }

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

        public async Task<ExchangeGrantBase> GetExchangeGrant(DotYouIdentity dotYouId)
        {
            //TODO: need to figure out how to secure this call; it has to be made when there is no master key since it's used to create a session

            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.FindOne(r => r.DotYouId == dotYouId));

            if (null == eg)
            {
                return null;
            }

            if (eg.StorageType == ExchangeGranteeType.App)
            {
                return AppExchangeGrant.FromLiteDbRecord(eg);
            }

            if (eg.StorageType == ExchangeGranteeType.Identity)
            {
                return IdentityExchangeGrant.FromLiteDbRecord(eg);
            }

            if (eg.StorageType == ExchangeGranteeType.YouAuth)
            {
                return YouAuthExchangeGrant.FromLiteDbRecord(eg);
            }

            throw new MissingDataException("Invalid Storage type on ExchangeGrantLiteDbRecord");
        }

        public async Task<bool> IsExchangeGrantRevoked(Guid id)
        {
            var g = await this.GetExchangeGrantInternal(id);
            return g.IsRevoked;
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

        /// <summary>
        /// Revokes the exchange grant and all associated access registrations
        /// </summary>
        /// <returns></returns>
        public async Task RevokeExchangeGrantAccess(Guid accessRegistrationId)
        {
            var accessReg = await this.GetAccessRegistration(accessRegistrationId);

            var allAccessRegistrations = await _systemStorage.WithTenantSystemStorageReturnList<AccessRegistration>(ACCESS_TOKEN_REG,
                s => s.Find(ar => ar.GrantId == accessReg.GrantId, PageOptions.All));

            //TODO: will we really have more than one here?

            //revoke all access regs
            foreach (var reg in allAccessRegistrations.Results)
            {
                reg.IsRevoked = true;
                _systemStorage.WithTenantSystemStorage<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Save(reg));
            }

            await RevokeExchangeGrant(accessReg.GrantId);
        }

        public async Task RevokeExchangeGrant(Guid grantId)
        {
            //revoke the grant
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Get(grantId));
            eg.IsRevoked = true;
            _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(eg));
        }

        public async Task RemoveExchangeGrantRevocation(Guid grantId)
        {
            //revoke the grant
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Get(grantId));
            eg.IsRevoked = false;
            _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(eg));
        }

        /// <summary>
        /// Revokes the exchange grant and all associated access registrations
        /// </summary>
        /// <returns></returns>
        public async Task RevokeIdentityExchangeGrantAccess(DotYouIdentity dotYouId)
        {
            var grant = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.FindOne(r => r.DotYouId == dotYouId));

            if (grant != null)
            {
                grant.IsRevoked = true;
                _systemStorage.WithTenantSystemStorage<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Save(grant));
            }

            //kill all associated access registrations (this will kill all browser logins)
            var allAccessRegistrations = await _systemStorage.WithTenantSystemStorageReturnList<AccessRegistration>(ACCESS_TOKEN_REG,
                s => s.Find(ar => ar.GrantId == grant.Id, PageOptions.All));

            //revoke all access regs
            foreach (var reg in allAccessRegistrations.Results)
            {
                reg.IsRevoked = true;
                _systemStorage.WithTenantSystemStorage<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Save(reg));
            }
        }

        public async Task<PagedResult<AppExchangeGrant>> GetAppExchangeGrantList(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            var recordPage = await _systemStorage.WithTenantSystemStorageReturnList<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION,
                s => s.Find(r => r.StorageType == ExchangeGranteeType.App, ListSortDirection.Ascending, sort => sort.Created, pageOptions));

            List<AppExchangeGrant> page = recordPage.Results.Select(AppExchangeGrant.FromLiteDbRecord).ToList();
            return new PagedResult<AppExchangeGrant>(recordPage.Request,
                recordPage.TotalPages,
                page);
        }

        public async Task<PagedResult<IdentityExchangeGrant>> GetIdentityExchangeGrantList(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            var recordPage = await _systemStorage.WithTenantSystemStorageReturnList<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION,
                s => s.Find(r => r.StorageType == ExchangeGranteeType.Identity, ListSortDirection.Ascending, sort => sort.Created, pageOptions));

            var page = recordPage.Results.Select(IdentityExchangeGrant.FromLiteDbRecord).ToList();
            return new PagedResult<IdentityExchangeGrant>(recordPage.Request,
                recordPage.TotalPages,
                page);
        }

        /////

        private async Task<ExchangeGrantBase> GetExchangeGrantInternal(Guid id)
        {
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrantBaseLiteDbRecord>(EXCHANGE_REGISTRATION, s => s.Get(id));

            if (null == eg)
            {
                return null;
            }

            if (eg.StorageType == ExchangeGranteeType.App)
            {
                return AppExchangeGrant.FromLiteDbRecord(eg);
            }

            if (eg.StorageType == ExchangeGranteeType.Identity)
            {
                return IdentityExchangeGrant.FromLiteDbRecord(eg);
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