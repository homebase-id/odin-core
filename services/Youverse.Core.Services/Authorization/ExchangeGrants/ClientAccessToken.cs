using System;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
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