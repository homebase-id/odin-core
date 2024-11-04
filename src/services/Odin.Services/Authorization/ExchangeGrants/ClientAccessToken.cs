using System;
using Odin.Core;
using Odin.Core.Cryptography.Data;

namespace Odin.Services.Authorization.ExchangeGrants
{
    public class EncryptedClientAccessToken
    {
        public SymmetricKeyEncryptedAes EncryptedData { get; set; }

        public ClientAccessToken Decrypt(SensitiveByteArray key)
        {
            var rawBytes = EncryptedData.DecryptKeyClone(key);
            return ClientAccessToken.FromPortableBytes(rawBytes.GetKey());
           
        }

        public static EncryptedClientAccessToken Encrypt(SensitiveByteArray icrKey, ClientAccessToken cat)
        {
            var bytes = cat.ToPortableBytes().ToSensitiveByteArray();
            var encryptedData = new SymmetricKeyEncryptedAes(icrKey, bytes);

            return new EncryptedClientAccessToken()
            {
                EncryptedData = encryptedData
            };
        }
    }

    public class ClientAccessToken
    {
        public Guid Id { get; set; }
        public SensitiveByteArray AccessTokenHalfKey { get; set; }

        public ClientTokenType ClientTokenType { get; set; }

        public SensitiveByteArray SharedSecret { get; set; }

        public ClientAuthenticationToken ToAuthenticationToken()
        {
            ClientAuthenticationToken authenticationToken = new ClientAuthenticationToken()
            {
                Id = this.Id,
                AccessTokenHalfKey = this.AccessTokenHalfKey,
                ClientTokenType = this.ClientTokenType
            };
            return authenticationToken;
        }

        public void Wipe()
        {
            this.AccessTokenHalfKey?.Wipe();
            this.SharedSecret?.Wipe();
        }

        public bool IsValid()
        {
            return ByteArrayUtil.IsStrongKey(AccessTokenHalfKey.GetKey()) &&
                   ByteArrayUtil.IsStrongKey(SharedSecret.GetKey()) &&
                   this.Id != Guid.Empty;
        }

        public byte[] ToPortableBytes()
        {
            var data1 = ByteArrayUtil.Combine(this.Id.ToByteArray(), AccessTokenHalfKey.GetKey(), new[] { (byte)this.ClientTokenType });
            var bytes = ByteArrayUtil.Combine(data1, this.SharedSecret.GetKey());
            return bytes;
        }

        /// <summary>
        /// Returns a base64 string of the <see cref="ToPortableBytes"/> method.  Wipes the intermediate bytes from memory.
        /// </summary>
        public string ToPortableBytes64()
        {
            var bytes = this.ToPortableBytes();
            var bytes64 = Convert.ToBase64String(bytes);
            bytes.ToSensitiveByteArray().Wipe();
            return bytes64;
        }

        public static ClientAccessToken FromPortableBytes64(string data64)
        {
            return FromPortableBytes(Convert.FromBase64String(data64));
        }

        public static ClientAccessToken FromPortableBytes(byte[] data)
        {
            var (idBytes, halfKeyBytes, secondSet) = ByteArrayUtil.Split(data, 16, 16, 17);
            var (type, sharedSecret) = ByteArrayUtil.Split(secondSet, 1, 16);

            return new ClientAccessToken()
            {
                Id = new Guid(idBytes),
                AccessTokenHalfKey = halfKeyBytes.ToSensitiveByteArray(),
                ClientTokenType = (ClientTokenType)type[0],
                SharedSecret = sharedSecret.ToSensitiveByteArray()
            };
        }
    }
}