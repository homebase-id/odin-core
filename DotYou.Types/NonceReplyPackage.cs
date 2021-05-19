namespace DotYou.Types
{
    /// <summary>
    /// Reply from the client during authentication which includes the client's
    /// password hashed using the data from <see cref="ClientNoncePackage.SaltPassword64"/>
    /// </summary>
    public sealed class NonceReplyPackage
    {
        public string Nonce64 { get; set; }

        public string NonceHashedPassword { get; set; }

        public string KeK64 { get; set; }

    }
}