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
        private readonly DotYouContext _context;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;

        public XTokenService(DotYouContext context, ILogger<XTokenService> logger, ISystemStorage systemStorage, IDriveService driveService)
        {
            _context = context;
            _systemStorage = systemStorage;
            _driveService = driveService;
        }

        /// <summary>
        /// Creates a new XToken and RSA Encrypted XToken Request
        /// </summary>
        /// <param name="publicKey"></param>
        /// <param name="driveIdList">The drives which are granted access</param>
        /// <returns></returns>
        public async Task<(XToken, string)> CreateXToken(byte[] publicKey, List<Guid> driveIdList)
        {
            _context.Caller.AssertHasMasterKey();

            var masterKey = _context.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var hostHalfKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteHalfKey);

            //TODO: encrypt shared secret using the ??
            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

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
                DriveKeyHalfKey = hostHalfKey,
                MasterKeyEncryptedDriveKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                ClientSharedSecretKey = sharedSecret,
                IsRevoked = false,
                DriveGrants = driveKeys
            };

            //TODO: RSA Encrypt
            var combinedBytes = ByteArrayUtil.Combine(hostHalfKey.KeyEncrypted, remoteHalfKey.GetKey(), sharedSecret);
            var request = Convert.ToBase64String(combinedBytes);

            combinedBytes.ToSensitiveByteArray().Wipe();
            keyStoreKey.Wipe();

            return (xtoken, request);
        }

        public async Task<(XToken, SensitiveByteArray)> CreateXTokenFromBits(List<Guid> driveIdlist, string rsaEncryptedXTokenBits)
        {
            _context.Caller.AssertHasMasterKey();

            var combinedBytes = Convert.FromBase64String(rsaEncryptedXTokenBits);

            var (hostHalfKey, remoteHalfKey, sharedSecret) = ByteArrayUtil.Split(combinedBytes, 16, 16, 16);

            var keyStoreKey = XorManagement.XorDecrypt(hostHalfKey, remoteHalfKey).ToSensitiveByteArray();

            //var hostHalfKeySBA = hostHalfKey.ToSensitiveByteArray();
            var remoteHalfKeySBA = remoteHalfKey.ToSensitiveByteArray();

            var newHostHalfKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, remoteHalfKeySBA, false);

            var clone = newHostHalfKey.DecryptKeyClone(ref remoteHalfKeySBA);
            Guard.Argument(ByteArrayUtil.EquiByteArrayCompare(keyStoreKey.GetKey(), clone.GetKey()), "matching keys").Require(v => v);
            clone.Wipe();

            var masterKey = _context.Caller.GetMasterKey();
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
                DriveKeyHalfKey = newHostHalfKey,
                MasterKeyEncryptedDriveKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                ClientSharedSecretKey = sharedSecret,
                IsRevoked = false,
                DriveGrants = driveKeys
            };

            combinedBytes.ToSensitiveByteArray().Wipe();
            keyStoreKey.Wipe();

            return (xtoken, remoteHalfKeySBA);
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
                driveKey = existingToken.DriveKeyHalfKey.DecryptKeyClone(ref halfKey);
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
                    DriveKeyHalfKey = hostHalfKey,
                    MasterKeyEncryptedDriveKey = existingToken.MasterKeyEncryptedDriveKey,
                    ClientSharedSecretKey = sharedSecret,
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