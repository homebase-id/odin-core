namespace Youverse.Services.Messaging.Chat
{
    public static class Cryptography
    {
        public static class Encrypt
        {
            public static string UsingPublicKey(string contactPublicKeyCertificate, string value)
            {
                return value;
            }
        }

        public static class Decrypt
        {
            public static string UsingCertificate(string certificate, string value)
            {
                return value;
            }
        }
    }
}