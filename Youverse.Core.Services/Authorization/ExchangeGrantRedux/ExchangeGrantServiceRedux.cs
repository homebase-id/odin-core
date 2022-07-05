using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Authorization.ExchangeGrantRedux
{
    /// <summary>
    /// Creates and manages grants the access of exchanging data.
    /// </summary>
    public class ExchangeGrantServiceRedux
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private const string EXCHANGE_REGISTRATION = "exreg";

        public ExchangeGrantServiceRedux(DotYouContextAccessor contextAccessor, ILogger<ExchangeGrantService> logger, ISystemStorage systemStorage, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
        }
        
        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grant"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(IExchangeGrant grant)
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();

            if (grant.IsRevoked)
            {
                throw new YouverseSecurityException("Cannot create Client Access Token for a revoked ExchangeGrant");
            }

            var mk = context.Caller.GetMasterKey();
            var grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref mk);

            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantKeyStoreKey);
            grantKeyStoreKey.Wipe();
            return (accessReg, clientAccessToken);
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

        public async Task<(IExchangeGrant, SensitiveByteArray)> CreateExchangeGrant(PermissionSet permissionSet, List<TargetDrive> targetDriveList, SensitiveByteArray masterKey = null)
        {
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grants = new List<DriveGrant>();

            List<StorageDrive> drives = new List<StorageDrive>();

            if (targetDriveList != null)
            {
                foreach (var targetDrive in targetDriveList)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var driveId = await _driveService.GetDriveIdByAlias(targetDrive, true);
                    var drive = await _driveService.GetDrive(driveId.GetValueOrDefault(), true);
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

            var grant = new ExchangeGrant()
            {
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