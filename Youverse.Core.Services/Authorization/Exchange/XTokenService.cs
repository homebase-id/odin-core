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
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Core.Services.Authorization.Exchange
{
    public class XTokenService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;

        public XTokenService(DotYouContextAccessor contextAccessor, ILogger<XTokenService> logger, ISystemStorage systemStorage, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
        }

        /// <summary>
        /// Creates a new XToken and RSA Encrypted XToken Request
        /// </summary>
        /// <param name="publicKey"></param>
        /// <param name="driveIdList">The drives which are granted access</param>
        /// <returns></returns>
        public async Task<(XToken, SensitiveByteArray, SensitiveByteArray)> CreateXToken(List<Guid> driveIdList)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var halfKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteGrantKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

            //TODO: encrypt shared secret using the keyStoreKey
            var encryptedSharedSecret = sharedSecret;

            var driveKeys = new List<XTokenDriveGrant>();

            foreach (var id in driveIdList)
            {
                var x = await _driveService.GetDrive(id);
                var storageKey = x.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new XTokenDriveGrant()
                {
                    DriveId = id,
                    XTokenEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
                };

                storageKey.Wipe();
                driveKeys.Add(dk);
            }

            var xtoken = new XToken()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                HalfKeyEncryptedDriveGrantKey = halfKeyEncryptedDriveGrantKey,
                MasterKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                KeyStoreKeyEncryptedSharedSecret = encryptedSharedSecret,
                IsRevoked = false,
                DriveGrants = driveKeys
            };

           
            keyStoreKey.Wipe();

            return (xtoken, remoteGrantKey, sharedSecret.ToSensitiveByteArray());
        }




        public async Task<(XToken, SensitiveByteArray)> xxxCreateXTokenFromOrigin(List<Guid> driveIdlist, string rsaEncryptedCredentials)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var combinedBytes = Convert.FromBase64String(rsaEncryptedCredentials);


            // We intentionally swap the local and remote key.
            // Sam creates a local and remote half. Send both to Frodo.
            // Frodo swaps (below) the two keys, so that what's local to
            // Sam is remote to Frodo and vice versa
            var (remoteHalfKey, localHalfKey, sharedSecret) = ByteArrayUtil.Split(combinedBytes, 16, 16, 16);

            var localSBA = localHalfKey.ToSensitiveByteArray();
            var remoteSBA = remoteHalfKey.ToSensitiveByteArray();

            var newHostHalfKey = new SymmetricKeyEncryptedXor(ref localSBA, remoteSBA, false, false);

            var keyStoreKey = newHostHalfKey.DecryptKeyClone(ref remoteSBA);
            Guard.Argument(ByteArrayUtil.EquiByteArrayCompare(XorManagement.XorEncrypt(remoteHalfKey, localHalfKey), keyStoreKey.GetKey()), "Sanity check failed: incoming keys do not match").Require(v => v);


            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var driveKeys = new List<XTokenDriveGrant>();

            foreach (var id in driveIdlist)
            {
                var x = await _driveService.GetDrive(id);
                var storageKey = x.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new XTokenDriveGrant()
                {
                    DriveId = id,
                    XTokenEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
                };

                storageKey.Wipe();
                driveKeys.Add(dk);
            }

            var xtoken = new XToken()
            {
                Id = Guid.NewGuid(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                HalfKeyEncryptedDriveGrantKey = newHostHalfKey,
                MasterKeyEncryptedDriveGrantKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                KeyStoreKeyEncryptedSharedSecret = sharedSecret,
                IsRevoked = false,
                DriveGrants = driveKeys
            };

            combinedBytes.ToSensitiveByteArray().Wipe();
            keyStoreKey.Wipe();

            return (xtoken, remoteSBA);
        }


        /// <summary>
        /// Creates a new XToken from an existing Xtoken by copying and re-encrypting the drives
        /// </summary>
        /// <returns></returns>
        public Task<(XToken, byte[])> CloneXToken(XToken existingToken, SensitiveByteArray halfKey)
        {
            Guard.Argument(existingToken, nameof(existingToken)).NotNull("Missing XToken for connection").Require(!existingToken.IsRevoked, x => "XToken is Revoked");

            SensitiveByteArray driveKey = null;
            SensitiveByteArray keyStoreKey = null;
            try
            {
                driveKey = existingToken.HalfKeyEncryptedDriveGrantKey.DecryptKeyClone(ref halfKey);
                keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
                var hostHalfKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteHalfKey);

                var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

                //clone and re-encrypt the drive keys
                var newDriveKeys = existingToken.DriveGrants.Select(dk =>
                {
                    var storageKey = dk.XTokenEncryptedStorageKey.DecryptKeyClone(ref driveKey);
                    var ndk = new XTokenDriveGrant()
                    {
                        DriveId = dk.DriveId,
                        XTokenEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref keyStoreKey, ref storageKey)
                    };
                    return ndk;
                }).ToList();
                var token = new XToken()
                {
                    Id = Guid.NewGuid(),
                    Created = DateTimeExtensions.UnixTimeMilliseconds(),
                    HalfKeyEncryptedDriveGrantKey = hostHalfKey,
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