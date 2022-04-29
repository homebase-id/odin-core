using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeRedux
{
    public class ExchangeGrantService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private readonly ICircleDefinitionService _cds;
        private readonly IAppRegistrationService _appRegistration;

        public ExchangeGrantService(DotYouContextAccessor contextAccessor, ILogger<ExchangeGrantService> logger, ISystemStorage systemStorage, IDriveService driveService, ICircleDefinitionService cds, IAppRegistrationService appRegistration)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
            _cds = cds;
            _appRegistration = appRegistration;
        }

        /// <summary>
        /// Creates a new Exchange registration and token based on system defaults
        /// </summary>
        /// <returns></returns>
        public async Task<(ExchangeRegistration, SensitiveByteArray, SensitiveByteArray)> CreateExchangeRegistration()
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();

            var rootCircle = _cds.GetRootCircle();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var remoteKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grants = new List<ExchangeDriveGrant>();
            foreach (var grant in rootCircle.Grants)
            {
                var appReg = await _appRegistration.GetAppContextBase(grant.Appid);
                if (appReg == null)
                {
                    //TODO: how to handle here?  warning?log?
                    continue;
                }

                foreach (var driveAlias in grant.DriveAliass)
                {
                    var driveId = appReg.GetDriveId(driveAlias);
                    var drive = await _driveService.GetDrive(driveId);
                    var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                    var dk = new ExchangeDriveGrant()
                    {
                        DriveAlias = driveAlias,
                        KeyStoreKeyEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
                    };

                    storageKey.Wipe();
                    grants.Add(dk);
                }
            }

            var reg = new ExchangeRegistration()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                RemoteKeyEncryptedKeyStoreKey = remoteKeyEncryptedKeyStoreKey,
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                KeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref sharedSecret),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = grants
            };


            keyStoreKey.Wipe();

            return (reg, remoteKey, sharedSecret);
        }

        /// <summary>
        /// Creates a new XToken from an <see cref="ExchangeRegistration"/> which can be given to remote callers for access to data.
        /// </summary>
        /// <returns></returns>
        public Task<(XTokenRegistration, SensitiveByteArray, SensitiveByteArray)> RegisterXToken(ExchangeRegistration registration, SensitiveByteArray remoteKey)
        {
            var parentKeyStoreKey = registration.RemoteKeyEncryptedKeyStoreKey.DecryptKeyClone(ref remoteKey);

            var xTokenRegistrationKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var xTokenEncryptedKeyStoreKey = new SymmetricKeyEncryptedXor(ref xTokenRegistrationKeyStoreKey, out var xToken);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new XTokenRegistration()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                RemoteKeyEncryptedKeyStoreKey = xTokenEncryptedKeyStoreKey,
                KeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(ref xTokenRegistrationKeyStoreKey, ref sharedSecret),
                IsRevoked = false,
                KeyStoreKeyEncryptedExchangeRegistrationKeyStoreKey = new SymmetricKeyEncryptedAes(ref parentKeyStoreKey, ref xTokenRegistrationKeyStoreKey)
            };

            parentKeyStoreKey.Wipe();
            xTokenRegistrationKeyStoreKey.Wipe();

            return Task.FromResult((reg, xToken, sharedSecret));
        }

        /// <summary>
        /// Gets an XToken Registration from a given xToken
        /// </summary>
        /// <param name="xToken"></param>
        /// <returns></returns>
        public async Task<XTokenRegistration> GetXTokenRegistration(SensitiveByteArray xToken)
        {
            return null;
        }
    }
}