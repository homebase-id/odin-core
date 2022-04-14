using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.Exchange
{
    public class ExchangeTokenService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private readonly ICircleDefinitionService _cds;
        private readonly IAppRegistrationService _appRegistration;

        public ExchangeTokenService(DotYouContextAccessor contextAccessor, ILogger<ExchangeTokenService> logger, ISystemStorage systemStorage, IDriveService driveService, ICircleDefinitionService cds, IAppRegistrationService appRegistration)
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
        public async Task<(ExchangeRegistration, SensitiveByteArray, SensitiveByteArray)> CreateDefault()
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();

            var rootCircle = _cds.GetRootCircle();

            //drives will come from the circle
            var driveIdList = rootCircle.Grants.SelectMany(x => x.DriveAliass);

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var remoteKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

            //TODO: encrypt shared secret using the keyStoreKey
            var encryptedSharedSecret = sharedSecret;

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
                        XTokenEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
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
                KeyStoreKeyEncryptedSharedSecret = encryptedSharedSecret,
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = grants
            };


            keyStoreKey.Wipe();

            return (reg, remoteKey, sharedSecret.ToSensitiveByteArray());
        }

        /// <summary>
        /// Creates a new XToken from an existing Xtoken by copying and re-encrypting the drives
        /// </summary>
        /// <returns></returns>
        public Task<(ChildExchangeRegistration, SensitiveByteArray, SensitiveByteArray)> CreateChildRegistration(ExchangeRegistration parentRegistration, SensitiveByteArray remoteKey)
        {
            var parentKeyStoreKey = parentRegistration.RemoteKeyEncryptedKeyStoreKey.DecryptKeyClone(ref remoteKey);
            var context = _contextAccessor.GetCurrent();

            var childKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var childRemoteKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedXor(ref childKeyStoreKey, out var childRemoteKey);

            var childSharedSecret = ByteArrayUtil.GetRndByteArray(16);

            //TODO: encrypt shared secret using the keyStoreKey
            var childEncryptedSharedSecret = childSharedSecret;

            var reg = new ChildExchangeRegistration()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                RemoteKeyEncryptedKeyStoreKey = childRemoteKeyEncryptedKeyStoreKey,
                KeyStoreKeyEncryptedSharedSecret = childEncryptedSharedSecret,
                IsRevoked = false,
                KeyStoreKeyEncryptedParentXTokenKeyStoreKey = new SymmetricKeyEncryptedAes(ref parentKeyStoreKey, ref childKeyStoreKey)
            };

            parentKeyStoreKey.Wipe();
            childKeyStoreKey.Wipe();

            return Task.FromResult((reg, childRemoteKey, childSharedSecret.ToSensitiveByteArray()));
        }
    }
}