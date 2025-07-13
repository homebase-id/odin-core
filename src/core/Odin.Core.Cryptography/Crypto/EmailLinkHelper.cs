
namespace Odin.Core.Cryptography.Crypto
{
    using Odin.Core.Util;
    using System;

    public class EmailLinkHelper
    {
        /// <summary>
        /// Initiates a password reset by splitting the secret using XOR, generating a token for the email link
        /// as well as a Ciper to store in the DB.
        /// </summary>
        /// <param name="secret">The secret to protect during the reset process.</param>
        /// <returns>A tuple containing the token string for the email link and the cipher bytes to store in the DB.</returns>
        public static (string TokenForEmail, byte[] CipherToStore) SplitSecret(byte[] secret)
        {
            // Split the secret into cipher and random using the provided function
            var (cipher, random) = XorManagement.XorSplitKey(secret);

            // Convert the random bytes to a GUID for string representation
            var guid = new Guid(random);
            string token = guid.ToString("N");

            // TODO: Store the cipher in the DB, using a generated row ID (e.g., new Guid().ToString("N")) as the primary key,
            // along with user ID, expiration timestamp (e.g., now + 1 hour), and any other metadata.
            // Example: Generate rowId = new Guid().ToString("N");
            // INSERT INTO ResetTokens (RowId, Cipher, UserId, Expiry) VALUES (@rowId, @cipher, @userId, DATEADD(hour, 1, GETUTCDATE()))

            return (token, cipher);
        }

        /// <summary>
        /// Recovers the original secret given the token from the email and the cipher loaded from the DB.
        /// </summary>
        /// <param name="token">The token string from the email link.</param>
        /// <param name="cipher">The cipher bytes loaded from the DB.</param>
        /// <returns>The recovered secret.</returns>
        public static byte[] AssembleSecret(string token, byte[] cipher)
        {
            // Parse the token back to GUID and extract the random bytes
            var guid = Guid.ParseExact(token, "N");
            byte[] random = guid.ToByteArray();

            // XOR is symmetric, so "decrypt" by XORing again
            byte[] secret = XorManagement.XorEncrypt(cipher, random);

            // After recovery, delete the DB row to enforce one-time use

            return secret;
        }

        /// <summary>
        /// Builds a reset URL string by combining the row ID and token as query parameters.
        /// </summary>
        /// <param name="baseUrl">The base URL for the reset endpoint (e.g., "https://yourapp.com/reset").</param>
        /// <param name="Id">The DB row ID (e.g., a GUID string).</param>
        /// <param name="token">The token string (GUID("N")).</param>
        /// <returns>The full URL string for the email link.</returns>
        public static string BuildResetUrl(string baseUrl, Guid Id, string token)
        {
            // Ensure URL-safe encoding if needed, but since both are hex ("N" format), they're safe
            return $"{baseUrl}?id={Uri.EscapeDataString(Id.ToString("N"))}&token={Uri.EscapeDataString(token)}";
        }

        /// <summary>
        /// Parses a reset URL string to extract the row ID and token.
        /// </summary>
        /// <param name="url">The full URL string from the email link.</param>
        /// <returns>A tuple containing the row ID and token strings.</returns>
        public static (Guid id, string Token) ParseResetUrl(string url)
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            var id = new Guid(query["id"]);
            string token = query["token"];
            id.AssertGuidNotEmpty();

            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Invalid URL: Missing token parameters.");

            return (id, token);
        }
    }
}
