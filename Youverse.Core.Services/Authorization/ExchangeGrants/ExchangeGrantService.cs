using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
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
        /// <param name="driveIdList">The list of drives which should be granted access </param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> RegisterExchangeGrant(List<Guid> driveIdList)
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grants = new List<DriveGrant>();

            foreach (var driveId in driveIdList)
            {
                var drive = await _driveService.GetDrive(driveId);
                var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new DriveGrant()
                {
                    DriveAlias = driveId,
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
                KeyStoreKeyEncryptedDriveGrants = grants
            };

            grantKeyStoreKey.Wipe();

            _systemStorage.WithTenantSystemStorage<ExchangeGrant>(EXCHANGE_REGISTRATION, s => s.Save(grant));

            return await this.RegisterClientAccessToken(grant, grantKeyStoreKey);
        }

        /// <summary>
        /// Creates a new XToken from an <see cref="ExchangeGrant"/> which can be given to remote callers for access to data.
        /// </summary>
        /// <returns></returns>
        public Task<(AccessRegistration, ClientAccessToken)> RegisterClientAccessToken(ExchangeGrant grant, SensitiveByteArray grantKeyStoreKey)
        {
            var accessKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var serverAccessKey = new SymmetricKeyEncryptedXor(ref accessKeyStoreKey, out var clientAccessKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new AccessRegistration()
            {
                Id = Guid.NewGuid(),
                GrantId = grant.Id,
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ClientAccessKeyEncryptedKeyStoreKey = serverAccessKey,
                AccessKeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(ref accessKeyStoreKey, ref sharedSecret),
                IsRevoked = false,
                AccessKeyStoreKeyEncryptedExchangeRegistrationKeyStoreKey = new SymmetricKeyEncryptedAes(ref accessKeyStoreKey, ref grantKeyStoreKey)
            };

            grantKeyStoreKey.Wipe();
            accessKeyStoreKey.Wipe();

            var cat = new ClientAccessToken()
            {
                Id = reg.Id,
                AccessTokenHalfKey = clientAccessKey,
                SharedSecret = sharedSecret
            };

            _systemStorage.WithTenantSystemStorage<AccessRegistration>(ACCESS_TOKEN_REG, s => s.Save(reg));
            
            return Task.FromResult((reg, cat));
        }

        /// <summary>
        /// Gets an XToken Registration from a given xToken
        /// </summary>
        /// <param name="xToken"></param>
        /// <returns></returns>
        public async Task<AccessRegistration> GetXTokenRegistration(SensitiveByteArray xToken)
        {
            return null;
        }
    }
}