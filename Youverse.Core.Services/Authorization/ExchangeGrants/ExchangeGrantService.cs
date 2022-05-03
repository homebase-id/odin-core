using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

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
        /// Creates a new Exchange registration and token based on system defaults
        /// </summary>
        /// <param name="permissionSet"></param>
        /// <param name="driveIdList">The list of drives which should be granted access </param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> RegisterExchangeGrant(PermissionSet permissionSet, List<Guid> driveIdList)
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grants = new List<DriveGrant>();
            
            List<StorageDrive> drives = new List<StorageDrive>();
            foreach (var id in driveIdList)
            {
                //Note: fail the whole operation if an invalid drive is specified (the true flag will ensure we throw an exception)
                var drive = await _driveService.GetDrive(id, true);
                drives.Add(drive);
            }
            //TODO: need to handle scenario when a new anon drive is added.  all grants must be updated
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);
            drives.AddRange(anonymousDrives.Results);
            
            foreach (var drive in drives)
            {
                var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new DriveGrant()
                {
                    DriveId = drive.Id,
                    DriveAllowsAnonymousReadAccess = drive.AllowAnonymousReads, //for debugging
                    KeyStoreKeyEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref grantKeyStoreKey, ref storageKey),
                    Permissions = DrivePermissions.Read //Hard coded until we support writing data into the system
                };

                storageKey.Wipe();
                grants.Add(dk);
            }

            var grant = new ExchangeGrant()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref grantKeyStoreKey),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = grants,
                PermissionSet = permissionSet
            };

            _systemStorage.WithTenantSystemStorage<ExchangeGrant>(EXCHANGE_REGISTRATION, s => s.Save(grant));

            var token = await this.CreateClientAccessTokenInternal(grant.Id, grantKeyStoreKey);
            grantKeyStoreKey.Wipe();

            return token;
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
            var mk = context.Caller.GetMasterKey();
            var grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref mk);

            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantId, grantKeyStoreKey);
            grantKeyStoreKey.Wipe();
            return (accessReg, clientAccessToken);
        }

        /// <summary>
        /// Creates a new <see cref="AccessRegistration"/> from an existing <see cref="AccessRegistration"/>
        /// </summary>
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessTokenFromExistingAccessRegistration(Guid accessRegId, SensitiveByteArray sourceAccessRegistrationHalfKey)
        {
            var sourceAccessRegistration = await this.GetAccessRegistration(accessRegId);
            var accessKeyStoreKey = sourceAccessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref sourceAccessRegistrationHalfKey);

            var grantKeyStoreKey = sourceAccessRegistration.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey.DecryptKeyClone(ref accessKeyStoreKey);
            var grant = await this.GetExchangeGrantInternal(sourceAccessRegistration.GrantId);

            var (newAccessRegistration, newClientAccessToken) = await this.CreateClientAccessTokenInternal(grant.Id, grantKeyStoreKey);

            accessKeyStoreKey.Wipe();
            grantKeyStoreKey.Wipe();

            return (newAccessRegistration, newClientAccessToken);
        }

        public async Task<(SensitiveByteArray, List<DriveGrant>)> GetDrivesFromValidatedAccessRegistration(Guid accessRegId, SensitiveByteArray accessTokenHalfKey)
        {
            var accessReg = await this.GetAccessRegistration(accessRegId);
            if (accessReg is {IsRevoked: false})
            {
                var grant = await this.GetExchangeGrantInternal(accessReg.GrantId);
                if (grant is {IsRevoked: false})
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
        /// the <see cref="AccessRegistration"/> and <see cref="ExchangeGrant"/> are revoked
        /// </summary>
        /// <param name="authToken"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ExchangeGrant)> GetAccessAndGrant(ClientAuthToken authToken)
        {
            var registration = await this.GetAccessRegistration(authToken.Id);

            registration.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            var grant = await this.GetExchangeGrantInternal(registration.GrantId);

            return (registration, grant);
        }

        public async Task<ExchangeGrant> GetExchangeGrant(Guid id)
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

            _systemStorage.WithTenantSystemStorage<ExchangeGrant>(EXCHANGE_REGISTRATION, s => s.Save(grant));
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

            _systemStorage.WithTenantSystemStorage<ExchangeGrant>(EXCHANGE_REGISTRATION, s => s.Save(grant));
        }

        public async Task<AccessRegistration> GetAccessRegistration(Guid id)
        {
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Get(id));
            return eg;
        }

        public async Task<PagedResult<ExchangeGrant>> GetExchangeGrantList(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            var page = await _systemStorage.WithTenantSystemStorageReturnList<ExchangeGrant>(EXCHANGE_REGISTRATION, s => s.GetList(pageOptions));
            return page;
        }

        /////

        private async Task<ExchangeGrant> GetExchangeGrantInternal(Guid id)
        {
            var eg = await _systemStorage.WithTenantSystemStorageReturnSingle<ExchangeGrant>(EXCHANGE_REGISTRATION, s => s.Get(id));
            return eg;
        }

        /// <summary>
        /// Creates a new XToken from an <see cref="ExchangeGrant"/> which can be given to remote callers for access to data.
        /// </summary>
        /// <returns></returns>
        private Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessTokenInternal(Guid grantId, SensitiveByteArray grantKeyStoreKey)
        {
            var accessKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var serverAccessKey = new SymmetricKeyEncryptedXor(ref accessKeyStoreKey, out var clientAccessKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new AccessRegistration()
            {
                Id = Guid.NewGuid(),
                GrantId = grantId,
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ClientAccessKeyEncryptedKeyStoreKey = serverAccessKey,
                AccessKeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(ref accessKeyStoreKey, ref sharedSecret),
                IsRevoked = false,
                AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey = new SymmetricKeyEncryptedAes(ref accessKeyStoreKey, ref grantKeyStoreKey)
            };

            grantKeyStoreKey.Wipe();
            accessKeyStoreKey.Wipe();

            //Note: we have to send both the id and the accesstokenhalfkey back to the server
            var cat = new ClientAccessToken()
            {
                Id = reg.Id,
                AccessTokenHalfKey = clientAccessKey,
                SharedSecret = sharedSecret
            };

            _systemStorage.WithTenantSystemStorage<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Save(reg));

            return Task.FromResult((reg, cat));
        }
        
        private async Task<List<DriveGrant>> GetAnonymousDriveGrants()
        {
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);
            var anonDriveGrants = anonymousDrives.Results.Select(drive => new DriveGrant()
            {
                DriveId = drive.Id,
                DriveAllowsAnonymousReadAccess = true,
                KeyStoreKeyEncryptedStorageKey = null,
                Permissions = DrivePermissions.Read
            }).ToList();

            return anonDriveGrants;
        }
    }
}