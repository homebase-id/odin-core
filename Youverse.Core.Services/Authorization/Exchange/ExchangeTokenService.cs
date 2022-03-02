using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Core.Services.Authorization.Exchange
{
    public class ExchangeTokenService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private readonly ICircleDefinitionService _cds;

        public ExchangeTokenService(DotYouContextAccessor contextAccessor, ILogger<ExchangeTokenService> logger, ISystemStorage systemStorage, IDriveService driveService, ICircleDefinitionService cds)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
            _cds = cds;
        }

        private async Task<List<Guid>> GetDefaultExchangeDrives()
        {
            var profileAppId = SystemAppConstants.ProfileAppId;
            var profileAppContext = await _appReg.GetAppContextBase(profileAppId);

            Guard.Argument(profileAppContext, nameof(profileAppContext)).NotNull("Invalid App");
            Guard.Argument(profileAppContext.DefaultDriveId, nameof(profileAppContext.DefaultDriveId)).Require(x => x.HasValue);

            //TODO: add all drives based on what what access was granted to the recipient
            return new List<Guid>
            {
                profileAppContext.GetDriveIdentifier(profileAppContext.DefaultDriveId.GetValueOrDefault())
            };
        }

        /// <summary>
        /// Creates a new Exchange registration and token based on system defaults
        /// </summary>
        /// <returns></returns>
        public async Task<(ExchangeRegistration, SensitiveByteArray, SensitiveByteArray)> CreateDefault()
        {
            var context = _contextAccessor.GetCurrent();
            context.Caller.AssertHasMasterKey();

            var root = _cds.GetRootCircle();

            //drives will come from the circle
            var driveIdList = root.Grants.SelectMany(x => x.DriveIdentifiers);

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var halfKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteGrantKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

            //TODO: encrypt shared secret using the keyStoreKey
            var encryptedSharedSecret = sharedSecret;

            var grants = new List<ExchangeDriveGrant>();

            foreach (var driveIdentifier in driveIdList)
            {
                var drive = await _driveService.GetDrive(context.AppContext.GetDriveId(driveIdentifier));
                var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new ExchangeDriveGrant()
                {
                    DriveIdentifier = driveIdentifier,
                    XTokenEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
                };

                storageKey.Wipe();
                grants.Add(dk);
            }

            var reg = new ExchangeRegistration()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                HalfKeyEncryptedDriveGrantKey = halfKeyEncryptedDriveGrantKey,
                MasterKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                KeyStoreKeyEncryptedSharedSecret = encryptedSharedSecret,
                IsRevoked = false,
                DriveGrants = grants
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
                    DriveGrants = newDriveKeys
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