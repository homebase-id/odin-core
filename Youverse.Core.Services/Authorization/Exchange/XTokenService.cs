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
        /// <param name="driveIdlist">The drives which are granted access</param>
        /// <returns></returns>
        public async Task<(XToken, string)> CreateXToken(byte[] publicKey, List<Guid> driveIdlist)
        {
            _context.Caller.AssertHasMasterKey();

            var masterKey = _context.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var hostHalfKey = new SymmetricKeyEncryptedXor(ref keyStoreKey, out var remoteHalfKey);

            //TODO: encrypt shared secret using the ??
            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

            var driveKeys = new List<DriveKey>();

            foreach(var id in driveIdlist)
            {
                var x = await _driveService.GetDrive(id);
                var storageKey = x.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new DriveKey()
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
                SharedSecretKey = sharedSecret,
                IsRevoked = false,
                DriveKeys = driveKeys
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

            var masterKey = _context.Caller.GetMasterKey();
            var keyStoreKey = XorManagement.XorDecrypt(hostHalfKey, remoteHalfKey).ToSensitiveByteArray();

            var hostHalfKeySBA = hostHalfKey.ToSensitiveByteArray();
            var remoteHalfKeySBA = remoteHalfKey.ToSensitiveByteArray();

            var newHostHalfKey = SymmetricKeyEncryptedXor.CombineHalfs(hostHalfKey.ToSensitiveByteArray(), remoteHalfKeySBA);
            var clone = newHostHalfKey.DecryptKeyClone(ref hostHalfKeySBA);
            Guard.Argument(ByteArrayUtil.EquiByteArrayCompare(keyStoreKey.GetKey(), clone.GetKey()), "matching keys").Require(v => v);
            clone.Wipe();

            var driveKeys = new List<DriveKey>();

            foreach (var id in driveIdlist)
            {
                var x = await _driveService.GetDrive(id);
                var storageKey = x.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

                var dk = new DriveKey()
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
                SharedSecretKey = sharedSecret,
                IsRevoked = false,
                DriveKeys = driveKeys
            };

            combinedBytes.ToSensitiveByteArray().Wipe();
            keyStoreKey.Wipe();

            return (xtoken, remoteHalfKeySBA);
        }

    }
}