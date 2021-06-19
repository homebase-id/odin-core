using System;
using System.Security.Cryptography;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Newtonsoft.Json;

namespace DotYou.Kernel.Cryptography
{
    [Obsolete("Waiting on Michael to just let it go")]
    public class IdentityKeySecurity 
    {

        // To be stored in the DB
        //
        [JsonProperty("saltPassword")]
        public byte[] SaltPassword { get; set; }  // The 16 byte salt used for the password

        [JsonProperty("saltKek")]
        public byte[] SaltKek { get; set; }       // The 16 byte salt used for the KEK

        [JsonProperty("hashPassword")]
        public byte[] HashPassword { get; set; }  // The Hashed password with SaltPassword, never compared directly with anything

        [JsonProperty("publicKey")]
        public string PublicKey { get; set; }            // The default at rest public key

        [JsonProperty("encryptedPrivateKey")]
        public string EncryptedPrivateKey { get; set; }  // The AES encrypted private key, encrypted with the KEK


        /// <summary>
        /// Use this for now on the server when creating new digital identities. When we make a flow
        /// to create them we'll have to send salts from the server, input the pwd on the client, do
        /// calculations and send the hashed key and kek back to the server (possibly public key encrypted).
        /// Will also create public private keys and all salts and hash values needed
        /// </summary>
        /// <param name="password"></param>
        public void SetRawPassword(string password)
        {
            // Client receives the two salt values from the server
            SaltPassword = YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE);
            SaltKek = YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE);

            // Client hashes the user password and salts and calculates the KEK
            HashPassword = KeyDerivation.Pbkdf2(password, SaltPassword, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS,CryptographyConstants.HASH_SIZE);
            var KeyEncryptionKey = KeyDerivation.Pbkdf2(password, SaltKek, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            // When Server receives the proper values it generates a key pair
            // Generate new public / private keys
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            PublicKey = rsaGenKeys.ToXmlString(false);
            EncryptedPrivateKey = rsaGenKeys.ToXmlString(true);

            // Server Encrypts the private key with the KEK and throws the KEK away.
            var (encrypted, iv) = AesCbc.EncryptStringToBytes_Aes(EncryptedPrivateKey, KeyEncryptionKey);
            EncryptedPrivateKey = Convert.ToBase64String(encrypted);
            // Don't forget to store the IV + EncryptedPrivateKey
            YFByteArray.WipeByteArray(KeyEncryptionKey);
        }


        // =================================================================================
        // Code above is only current production code
        // All code below here is just various temp code 
        // =================================================================================

        /// <summary>
        /// Validate if the password on the client has been entered correctly. 
        /// </summary>
        /// <param name="parameter">Base64NonceHashedPassword$Base64KEK""</param>
        /// <returns>KEK byte[]</returns>
        public byte[] ValidatePassword(string parameter)
        {
            string[] ss = parameter.Split("$");

            if (ss.Length != 2)
                throw new ArgumentException("Expects one string with two base64 $ delimited values");

            // Get the nonce sent to the client somewhere from this server
            byte[] nonce = YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE);

            byte[] HashedNonceHashedPassword = Convert.FromBase64String(ss[0]);

            if (HashedNonceHashedPassword.Length != 16)
                throw new ArgumentException("Nonce hash is not 16 bytes");

            byte[] KeyEncryptionKey = Convert.FromBase64String(ss[1]);

            if (KeyEncryptionKey.Length != 16)
                throw new ArgumentException("KEK is not 16 bytes");

            var ServerHashedNonceHashedPassword = KeyDerivation.Pbkdf2(Convert.ToBase64String(HashPassword), nonce, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            if (HashedNonceHashedPassword != ServerHashedNonceHashedPassword)
                throw new ArgumentException("Incorrect password");

            // Test the validity of the KEK
            // I guess decrypt the EncryptedPrivateKey
            // And then somehow test that the key is valid unless AES will automatically detect the key is wrong.

            return KeyEncryptionKey;
        }



        public void PrepareNewPassword()
        {
            SaltPassword = YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE);
            SaltKek = YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE);
        }

        public void SendToClient()
        {
            byte[] nonce;

            nonce = YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE);
            // Send the:
            // SaltPassword
            // SaltKek
            // nonce

            // Where can I deal with these temp data?
        }

        // Code to run on the client
        public string ClientNewPassword(string saltPassword64, string saltKek64, string nonce64, string newPassword)
        {
            var _SaltPassword = Convert.FromBase64String(saltPassword64);
            var _SaltKek = Convert.FromBase64String(saltKek64);
            var _Nonce = Convert.FromBase64String(nonce64);

            // Hash the user password + user salt
            var HashedPassword = KeyDerivation.Pbkdf2(newPassword, _SaltPassword, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var HashedNonceHashedPassword = KeyDerivation.Pbkdf2(Convert.ToBase64String(HashedPassword), _Nonce, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var KeyEncryptionKey = KeyDerivation.Pbkdf2(newPassword, _SaltKek, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            // Server is to discard the KeyEncryptionKey as soon as it has created a key-set and encrypted the private key
            // The KEK is delivered to the server on password creation and should not again be calculated by the client
            //
            return Convert.ToBase64String(HashedNonceHashedPassword) + "$" + Convert.ToBase64String(KeyEncryptionKey) + "$" + Convert.ToBase64String(HashedPassword);
        }


        // Code to run on the client
        public string ClientPasswordVerify(string saltPassword64, string saltKek64, string nonce64, string password)
        {
            var _SaltPassword = Convert.FromBase64String(saltPassword64);
            var _SaltKek = Convert.FromBase64String(saltKek64);
            var _Nonce = Convert.FromBase64String(nonce64);

            // Hash the user password + user salt
            var HashedPassword = KeyDerivation.Pbkdf2(password, _SaltPassword, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var HashedNonceHashedPassword = KeyDerivation.Pbkdf2(Convert.ToBase64String(HashedPassword), _Nonce, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var KeyEncryptionKey = KeyDerivation.Pbkdf2(password, _SaltKek, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            return Convert.ToBase64String(HashedNonceHashedPassword) + "$" + Convert.ToBase64String(KeyEncryptionKey);
        }



        // Creating a new password
        //
        // Server
        //   Server creates:
        //       password salt to user
        //       KEK salt
        //       nonce to the user
        //
        // Client
        //   Receive the three values above.
        //   Get the password from the user
        //   Deliver to the server:
        //      HashedPassword = Hash(pwd, passwordSalt)
        //      NonceHashedPassword = Hash(Hash(pwd, passwordSalt), nonce)
        //      KEK = Hash(pwd, KekSalt)
        //
        // Upon receipt the server creates a public / private key pair and encrypts the private key with the KEK
        // KEK is discarded on the server. The other values are saved. 

        // If the length equals the 
        //
        public static byte[] CreateKeyDerivationKey(byte[] salt, string password, int nBytes)
        {
            var ra = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, nBytes);
            return ra;
        }

        public static string PasswordFlow(string userPassword, byte[] userSalt)
        {
            // Hash the user password + user salt
            var hpwd = KeyDerivation.Pbkdf2(userPassword, userSalt, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            // Generate an 8 byte nonce using RNGCryptoServiceProvider
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            byte[] nonce = new byte[CryptographyConstants.NONCE_SIZE];
            rng.GetBytes(nonce);

            // Hash the hpwd byte[] converted to Base64 with the nonce byte array as salt
            var final = KeyDerivation.Pbkdf2(Convert.ToBase64String(hpwd), nonce, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            return Convert.ToBase64String(nonce) + "$" + Convert.ToBase64String(final);
        }


        // Creating a new password
        //
        // Server
        //   Server creates:
        //       password salt to user
        //       KEK salt
        //       nonce to the user
        //
        // Client
        //   Receive the three values above.
        //   Get the password from the user
        //   Deliver to the server:
        //      HashedPassword = Hash(pwd, passwordSalt)
        //      NonceHashedPassword = Hash(Hash(pwd, passwordSalt), nonce)
        //      KEK = Hash(pwd, KekSalt)
        //
        // Upon receipt the server creates a public / private key pair and encrypts the private key with the KEK
        // KEK is discarded on the server. The other values are saved. 

        /// <summary>
        /// Runs server side. Based on the newpassword string it does needed setup
        /// </summary>
        /// <param name="newpassword"></param>
        public static void CreateFirstPassword(string newPassword)
        {
            // Generate a 16 byte userSalt using RNGCryptoServiceProvider
            // We store this userSalt value in the DB
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            byte[] userSalt = new byte[CryptographyConstants.HASH_SIZE];
            rng.GetBytes(userSalt);

            // Hash the new password + user salt
            // We store this value in the DB
            var hashPassword = KeyDerivation.Pbkdf2(newPassword, userSalt, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);


            // We generate a new 2048 bit public / private key to encrypt the user's data
            //
            RSACryptoServiceProvider rsaKeys = new RSACryptoServiceProvider(2048);

            // We generate a KEK salt
            // We store this in the DB
            byte[] KekSalt = new byte[CryptographyConstants.HASH_SIZE];
            rng.GetBytes(KekSalt);

            // We generate a 16-byte KEK for encrypting the private key
            var keyEncryptionKey = KeyDerivation.Pbkdf2(newPassword, KekSalt, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            // Using the KEK, we encrypt the private key (AES)
            //


            // We store the resulting AES IV (nonce) plus encrypted data in base64 separated by the '$' character


            // Store public key in the DB
            // Store the encrypted private key in the DB
        }



    }
}