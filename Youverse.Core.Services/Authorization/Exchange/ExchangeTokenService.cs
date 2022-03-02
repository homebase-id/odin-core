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
            var driveIdList = rootCircle.Grants.SelectMany(x => x.DriveIdentifiers);

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var halfKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteGrantKey);

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
                
                foreach (var driveIdentifier in grant.DriveIdentifiers)
                {
                    var driveId = appReg.GetDriveId(driveIdentifier);
                    var drive = await _driveService.GetDrive(driveId);
                    var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                    var dk = new ExchangeDriveGrant()
                    {
                        DriveIdentifier = driveIdentifier,
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
                HalfKeyEncryptedDriveGrantKey = halfKeyEncryptedDriveGrantKey,
                MasterKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                KeyStoreKeyEncryptedSharedSecret = encryptedSharedSecret,
                IsRevoked = false,
                DriveGrants = grants,
                CircleGrants = new List<Guid>() {rootCircle.Id}
            };


            keyStoreKey.Wipe();

            return (reg, remoteGrantKey, sharedSecret.ToSensitiveByteArray());
        }

        /// <summary>
        /// Creates a new XToken from an existing Xtoken by copying and re-encrypting the drives
        /// </summary>
        /// <returns></returns>
        public Task<(ExchangeRegistration, byte[])> TransferXToken(ExchangeRegistration existingToken, SensitiveByteArray remoteGrantKey)
        {
            Guard.Argument(existingToken, nameof(existingToken)).NotNull("Missing XToken for connection").Require(!existingToken.IsRevoked, x => "Exchange Registration is Revoked");

            SensitiveByteArray driveKey = null;
            SensitiveByteArray keyStoreKey = null;
            try
            {
                driveKey = existingToken.HalfKeyEncryptedDriveGrantKey.DecryptKeyClone(ref remoteGrantKey);
                keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
                var halfKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteHalfKey);

                var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

                //clone and re-encrypt the drive keys
                var newDriveKeys = existingToken.DriveGrants.Select(dk =>
                {
                    var storageKey = dk.XTokenEncryptedStorageKey.DecryptKeyClone(ref driveKey);
                    var ndk = new ExchangeDriveGrant()
                    {
                        DriveIdentifier = dk.DriveIdentifier,
                        XTokenEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
                    };
                    return ndk;
                }).ToList();

                var token = new ExchangeRegistration()
                {
                    Id = Guid.NewGuid(),
                    Created = DateTimeExtensions.UnixTimeMilliseconds(),
                    HalfKeyEncryptedDriveGrantKey = halfKeyEncryptedDriveGrantKey,
                    MasterKeyEncryptedDriveGrantKey = existingToken.MasterKeyEncryptedDriveGrantKey,
                    KeyStoreKeyEncryptedSharedSecret = sharedSecret,
                    IsRevoked = false,
                    DriveGrants = newDriveKeys,
                    CircleGrants = existingToken.CircleGrants
                };

                return Task.FromResult((token, remoteHalfKey.GetKey()));
            }
            finally
            {
                driveKey?.Wipe();
                keyStoreKey?.Wipe();
            }
        }
    }
}