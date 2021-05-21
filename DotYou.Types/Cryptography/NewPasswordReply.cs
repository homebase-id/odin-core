namespace DotYou.Types.Cryptography
{
    /// <summary>
    /// Holds the values when the client creates a new Digital Identity
    /// </summary>
    public sealed class NewPasswordReply
    {
        public string Nonce64 { get; set; }

        public string HashedPassword64 { get; set; }

        public string NonceHashedPassword64 { get; set; }
        
        public string KeK64 { get; set; }
    }
}