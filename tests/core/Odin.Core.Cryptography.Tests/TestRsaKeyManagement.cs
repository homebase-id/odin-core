using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Core.Cryptography.Tests
{
    public class TestRsaKeyManagement
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void RsaKeyCreateTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(key, 1);
        }

        [Test]
        public void RsaKeyEncryptPublicTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(key, 1);
            byte[] data = {1, 2, 3, 4, 5};

            var cipher = rsa.Encrypt(data); // Encrypt with public key 
            var decrypt = rsa.Decrypt(key, cipher); // Decrypt with private key

            if (ByteArrayUtil.EquiByteArrayCompare(data, decrypt) == false)
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void RsaKeySignatureTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(key, 1);
            byte[] data = { 1, 2, 3, 4, 5 };

            var signature = rsa.Sign(key, data); // Sign with the private key 
            var isOK = rsa.VerifySignature(data, signature); // Verify with public key

            if (isOK == false) 
                Assert.Fail();
        }

        [Test]
        public void RsaKeyInvalidKeyTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(key, 1);
            byte[] data = { 1, 2, 3, 4, 5 };

            var cipher = rsa.Encrypt(data); // Encrypt with public key 
            var decrypt = rsa.Decrypt(key, cipher); // Decrypt with private key

            if (ByteArrayUtil.EquiByteArrayCompare(data, decrypt) == false)
                Assert.Fail();

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            
            try
            {
                decrypt = rsa.Decrypt(junk, cipher);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }

        [Test]
        public void RsaKeyCreateInvalidTest()
        {
            try
            {
                var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
                var rsa = new RsaFullKeyData(key, 0);
            }
            catch
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void RsaKeyCreateTimerTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var rsa = new RsaFullKeyData(key, 0, seconds: 2);

            if (!rsa.IsValid())
                Assert.Fail();

            if (rsa.IsExpired())
                Assert.Fail();

            if (rsa.IsDead())
                Assert.Fail();

            Thread.Sleep(3000); // The key is now 1 second expired.

            if (!rsa.IsExpired())
                Assert.Fail();

            if (rsa.IsValid())
                Assert.Fail();

            if (rsa.IsDead())
                Assert.Fail();

            Thread.Sleep(3000); // The key is now 4 seconds expired, so dead.

            if (!rsa.IsExpired())
                Assert.Fail();

            if (rsa.IsValid())
                Assert.Fail();

            if (!rsa.IsDead())
                Assert.Fail();

            Assert.Pass();
        }


        /// <summary>
        /// Seems like the RSACng max length is 190 bytes. This will pass 190 bytes and fail 191 bytes.
        /// </summary>
        [Test]
        public void RsaKeyLengthTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var rsa = new RsaFullKeyData(key, 1);

            // 190 chars
            var myData = "01234567890123456789012345678901234567890123456789" + "01234567890123456789012345678901234567890123456789" +
                         "01234567890123456789012345678901234567890123456789" + "0123456789012345678901234567890123456789";

            byte[] toEncryptData = Encoding.ASCII.GetBytes(myData);
            byte[] cipher = rsa.Encrypt(toEncryptData);
            if (cipher.Length != 256)
                Assert.Fail();

            // Now try with 191 bytes
            var tooLong = myData + "0";
            byte[] toEncryptData2 = Encoding.ASCII.GetBytes(tooLong);

            try
            {
                byte[] cipher2 = rsa.Encrypt(toEncryptData2);
            }
            catch
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }


        [Test]
        public void RsaKeyCrossJSTest()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var rsa = new RsaFullKeyData(key, 1);
            var publicKey = rsa.publicDerBase64();
            var privateKey = rsa.privateDerBase64(key);

            // max chars
            var myData = "01234567890123456789012345678901234567890123456789" + "01234567890123456789012345678901234567890123456789" +
                         "01234567890123456789012345678901234567890123456789" + "0123456789012345678901234567890123456789";
            byte[] toEncryptData = Encoding.ASCII.GetBytes(myData);

            byte[] cipher = rsa.Encrypt(toEncryptData);
            var base64cipher = Convert.ToBase64String(cipher);
            Console.WriteLine($"Public Key: {publicKey}");
            Console.WriteLine($"Private Key: {privateKey}");
            Console.WriteLine($"Encrypted data: {base64cipher}");
            Assert.Pass();
        }


        // ===== GENERIC BOUNCY CASTLE TEST =====

        [Test]
        public void BouncyCastleOAEPRoundTripPass()
        {
            // Generate an asymmetric key with BC, 2048 bits
            RsaKeyPairGenerator r = new RsaKeyPairGenerator();
            r.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair keys = r.GenerateKeyPair();

            // Extract the public and the private keys
            var privInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var pubInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);

            // DER encode the extracted public and private keys
            var privateKeyDer = privInfo.GetDerEncoded();
            var publicKeyDer = pubInfo.GetDerEncoded();

            // Recreate the public and private keys from the extracted DER encoded versions
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKeyDer);
            var privateKeyRestored = PrivateKeyFactory.CreateKey(privateKeyDer);

            // Go to this site:
            // https://lapo.it/asn1js/
            // To test out the key. In the immediate window CTRL+ALT+I
            // get the value of ?pubInfo and of Convert.ToBase64String(publicKeyDer). 
            // Both can be pasted into the site to compare.

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);

            var testStr = "En frøk ræv";
            var dataToEncrypt = Encoding.UTF8.GetBytes(testStr);
            var cipherBlock = cipher.DoFinal(dataToEncrypt);

            // Now let's try to decrypt it

            var cipher2 = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher2.Init(false, privateKeyRestored);

            var roundTrip = cipher2.DoFinal(cipherBlock);
            string chkStr = Encoding.UTF8.GetString(roundTrip);

            if (chkStr == testStr)
                Assert.Pass();
            else
                Assert.Fail();
        }

        //
        // ===== GENERIC RSA TESTS =====
        //
        // I've taken the JS that imports the key from above, and encrypts the 150 bytes with the public key
        // into cipher64 below

        [Test]
        public void RsaCrossJSTest2()
        {
            var cipher64 = "CMp08Cbr3ExoPuXcJO+9HnKQaC1bvifZxSLxJw1NZk4tZCLmBJpDwYUfGl26ffEyhc4Og01nekVwKf15Rf/bjPk5Cu6gnbGsSCB18eUUJgvPWPP34dF2Oh8jECNczQKp8q7QbujFv7Tsou+rumbbtDTnHziC7r9BBZsDW6xLY3jSRFyWJFsExHzZCd/vX4CCpHiSUZRB9Z1CnxwQ8jIjto+dRAcjE0ggeMCtoz78q43eG9CglUhiwQNTTF85goffzQqAjnNW0+1mdcay0pFGS+SGK4QODzPZX3VRT4PKh2aGhSUiVvzmKL6ptZCrIowOKnssiC89sOeIilStL5fhkw==";
            var fullKey64 = "MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQCrISxm5mkLUlSq3W7Yva9kDkh9QAW6mrfsWJzc0Ah+RTN0ohsIvDInuxzjol2mw7lFa6jsGCbCh0VJrhN7Mt/MRbNWvcUlZA71dON40yL856iWVYOxL9TpJgnyFIbpO3Gsn7SttUXFrijrvymlVt5eI2acPSra3g0Y/FRByajEqv528wkT+F0AxSn+Dt/vLZJq9xT9t1MakU624pzLd07hvWmHhRM8whGn2UbP+nTrIiPPHECQ0whghIwJ4AYsru3V8wDR8Mqx0+7juen5oXW9bQF1+qwXyjo3kEy6p+sULnlLPiT36PD0LPJTHiieX/Si/72eQX2CLTPrTMX0i2b9AgMBAAECggEAMbI4islux/Lo05XqktbDEHN1aaol/8LelqxFIXrofILsJnrNDwRYLGGSSijkuYEtVJOnQqjg2K0f2f3LeoOTqmazZgVGM02TaoS/al8mUfuUYdQDonkZg3ugd8SuSR0SLedTOP7jfDzPdWbWWUWY3g25xrWctGK3uwHMFi7R7Aqit/U9poC50HJkfyIFsoBSKEv80V5PJbsiHD+fE/9Jk/oo6xNOztuE9NwHFu/deOroNxvHhgjW9Q/bhXhQ/XwhlKSSgcgsapqri7nvqWSigeIG1XRESKe7qPU8NxdL7h7BpXfaTS6Maay6aqO/h3hfbKqpVMB+54Zltu8nuFnaoQKBgQDTs75ndP4JDg8lGqgfmaquRnadd/pCSovHzKrdbWXygZZx1UgfZXBEuZj1yvyi7vvl70DBp80Z5RwEgpVu9ZbZIbTtB4mHqzWIT0ZlM0fW483n5n5kSmNArD+fQX0CQHmmtmJqDouWSMv63BKrZbXHrW64hn50n4Xlu0UBw5wZYwKBgQDO8BQqcsXPkv2eo9MnytS36EQjMXBxEU8iGCSn98EeIlW2F6fjx0Etcz3lXxLaJxR4mfdG7ffgFz2xiBnB247kl8BLcjx1PdWFYJ4xjIG0tukf6paDdW6qHrn03fMFKaYw5ablx5ByVxsudhnhJWd5L4oNYvSXyfCfrtW/I+ScHwKBgFr3tIx+IB7B9M4Ly0xw2n+ydYuqn1XW9INxNcaaGKGA/6WAcVJUY06Utd6AT9ivenxON3Q/Z4mGAmkJt66LRzucGUN05qrubb1Z2zTnOSpkjvjj+VGdCVMj8N685DuQevWhD17lSyPTuhrccAVIWjkoFBikajgwx/d0Ze2hITVjAoGBAKxvXT5p2O842uFgPcmAuHRutKhmv/1XoQsV9yWHy4Iiti0/1QR2upb22nLRIFJsEiDUmzqdfNlcRGo0sNHa9F0DHpc/n6VKWywC8I71N/ewGt4fikAMkKRtaiLi92gr5nIES2hZPMIqV1oFy1bS5kATHwQ8mvgIq9tDwpS9gfedAoGBAMpSZUEkseFezu9bLL46Ca0uoDl3fegZFrLHbcLqlKn7mPRrqCc0KEM+P2BGQgSwBinzwU+SBHaspSJsIf39Z8N1h+KjEO9EeydFoACgLxQjp+TmJzAiczEvE1rN8bmOS615skQhJuEMtDc2/fnPrePfeT9eFp0ZpO8rwdrIlPbS";

            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            // var myRsa = new RSACng();
            var myRsa = new RsaFullKeyData(key, Convert.FromBase64String(fullKey64));

            var bin = Convert.FromBase64String(cipher64);
            var orgData = myRsa.Decrypt(key, bin);

            var my256 = "01234567890123456789012345678901234567890123456789" + "01234567890123456789012345678901234567890123456789" +
                        "01234567890123456789012345678901234567890123456789"; // + "01234567890123456789012345678901234567890123456789";// +

            if (Encoding.ASCII.GetString(orgData) != my256)
                Assert.Fail();
            else
                Assert.Pass();
        }


        // I've taken the JS that imports the key from above, and encrypts the 150 bytes with the public key
        // into cipher64 below
        /* [Test]
        public void RsaCrossJSTest3()
        {
            var myRsa = new RSACryptoServiceProvider(2048);

            var publicKey = myRsa.ExportSubjectPublicKeyInfo();
            var privateKey = myRsa.ExportPkcs8PrivateKey();

            var publicKey64  = Convert.ToBase64String(publicKey);
            var privateKey64 = Convert.ToBase64String(privateKey);

            var cipher64 = "PzN5xT7Qc8EUUcrC05WTvw92ujmgGZw2ngh8WLvRBJRLfxWDKiUR7znZ3kyCJuZUi6kmM2+wksQPVOwvYcGSZQsHUXVUBivYKKn01FNX8kzfuDgL7hKPqY3fY+3rNsNS6JCBaaAcWwGXgAJRfzF0HYGIkNur5AV4gco7nUVu5sxQpfi7GanwhqPxbJbfSqjhTio/isNM5+vjBs7DQ2QzcyyHzg1lcosoGPumTF+Rr9eHYrMXelE8eUW2F4IV/FFMwGfunMApq+MmoqNHcahLxkQgWhNax5qYJVWyYVwHJ9n9JuhQPC36NmuxjNDQ3d3ctxk/DbHqzFITBAYVMBWCaQ==";
            var fullKey64 = "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDpO3vlaB3uJU6UWT2xOX/53+iKIw80fk20dorq448TUZEJJdZu+9jjr6+uA0iTcNkINI8KTbp7RT4C8H+bC1kVqpxGuDets7aNwZBbJthl1MD5SpA1WLUFLFEJGUWm0agz6l0aecAbyjzRXBZmWvJQ+IxczYUX8GEdI7EOgx9eAUomfFz7gy93O7mpgD7+K+OAKZP7NSFvTR5JacIKCcHhJdEU/9VkHaKwf+i98nqRMYMT57UK3eBAPqay3QJxJ71i9eFiEAOqM47mcsirhv4FegzXxx7irL23vn8CZ1b0907HsKKRUMc2uFXBgC32s1Od+1b3IA+ZRf2kbJ2gCXKpAgMBAAECggEAUZXSQCxMk/qO40vYTb9Mag8OHAwpjHZGHkN9Uq8pZFua/XUz7nzAoNza+mcBoznNYZZpFvSbr/VHvOV97bFphy+4HPDh4SxFRo8YPRp4hh6HJm0TxuVx5Q5chm9FsxYR3Z801EcUkWQMJDwvRby4mORozSnDTd1zSysqC2aIuWvVHZux1OKGO/6J6fUBussNKgtT5V8phkK3Hgez/7c8pAOoDsb8YV+qUmRkKedz2E6pCHaDM21g0mk9IYYNPmzpOq7jzw8+m73kSQBHO9RfS6S5S58eYPd79pBdIWn7l4qOj3BoRK8C0HOYhwCemgU1/D9ZusrRIYL2dslWDxVyNQKBgQDzz5YMrmOmdKLDNTUdymntqblo+xEg+Ld6dB8j6P9naqCSAdDlLW5FQRB0SjiyUYn8I+A4bxOY6EAf4wkBMPrwGOUwPbiFZL4Hz2lcSlsqthLDljaxcfGwLFzr4G/xkv/gjWBAYpvW/HIZhcCC2fph1Z0tK9Hzr3HNboR+2vca6wKBgQD05IIyrii8P7/sxF8wWL6k6+3I1g1YblGBvl4xXtXMVvWsZL7a28wq7U+wrsfBN1oPEj/iEjr+e34zyfmEVT7AOofho+C0OsziufGUWw5ghqVJAa9ngoKEFHvrteopyl/5Nd61UZEK5mr8UZ93txhU5P7xnLX+ve5Rq1QPiJYbuwKBgBCgJjZFKgxuxa6UEUQvyltniHotLLTX4QMbqgfz2n692ac7Mnh+SZe1YR7c9NLMFqG3/JE8mdSCeeTywWlwYpw+xlosy0llXkQAE8o0U9Usx0jJFH+zKmz+CXQYQOnzQTmZymd5kfDuFAXDhiYmIRnMzEQJSe7ZFuSQVb6kxdbzAoGAXh+53wrLQ1dpR/JN98IUPEUl1oxXAscb8rcdcvJVUD2YHVN3e50BQvqFJ4513lCM/7/u59BD9m22muclTPSKss2MTnBzPDJhbz8yl+fLhdQakQ3hwfIKggNxga4gu0E6VAmdeKlKCxt2wVYJ6bRo2LBPQMQPu0J6587m9zVzJGMCgYEAola3rvB2lWoSUUSIj8atE3f1sLw8Hu5V0x2BTQgPtkGV1JtFzSJdRsEqPxUaN6nZfkb9AdyvFSyjV9SgqzO9ggRZLELMcAk6LQ5F+mc977eo8mfdIzusI++gfA6kVmzcp+lMHHWcgzW7Tt9r1BS54MrV/2JfFYnY2uP6HS7B750=";

            myRsa.ImportPkcs8PrivateKey(Convert.FromBase64String(fullKey64), out int _);
            var bin = Convert.FromBase64String(cipher64);
            var orgData = myRsa.Decrypt(bin, RSAEncryptionPadding.OaepSHA256);

            var my256 = "01234567890123456789012345678901234567890123456789" + "01234567890123456789012345678901234567890123456789" +
                         "01234567890123456789012345678901234567890123456789";// + "01234567890123456789012345678901234567890123456789";// +

            if (Encoding.ASCII.GetString(orgData) != my256)
                Assert.Fail();
            else
                Assert.Pass();
        }*/


        [Test]
        public void RSABasicTest()
        {
            // Generate a public/private key using RSA  
            RSACryptoServiceProvider RSA = new RSACryptoServiceProvider(2048); // 2048 bits

            // Read public and private key in a string  
            string str = RSA.ToXmlString(true);
            Console.WriteLine(str);

            // Get key into parameters  
            RSAParameters RSAKeyInfo = RSA.ExportParameters(true);
            Console.WriteLine($"Modulus: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.Modulus)}");
            Console.WriteLine($"Exponent: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.Exponent)}");
            Console.WriteLine($"P: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.P)}");
            Console.WriteLine($"Q: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.Q)}");
            Console.WriteLine($"DP: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.DP)}");
            Console.WriteLine($"DQ: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.DQ)}");
        }


        [Test]
        public void RSAPublicEnrcryptDecryptTest()
        {
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider();
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello world";
            byte[] toEncryptData = Encoding.ASCII.GetBytes(mySecret);

            //Encode with public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.FromXmlString(publicXml);
            byte[] encryptedRSA = rsaPublic.Encrypt(toEncryptData, false);
            string EncryptedResult = Encoding.Default.GetString(encryptedRSA);

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider();
            rsaPrivate.FromXmlString(privateXml);
            byte[] decryptedRSA = rsaPrivate.Decrypt(encryptedRSA, false);
            string originalResult = Encoding.Default.GetString(decryptedRSA);

            if (originalResult == mySecret)
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void RSAPublicSignTest()
        {
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello world";
            byte[] toEncryptData = Encoding.ASCII.GetBytes(mySecret);

            //Sign with private key
            var rsaPrivate = new RSACryptoServiceProvider();
            rsaPrivate.FromXmlString(privateXml);
            byte[] signedRSA = rsaPrivate.SignData(toEncryptData, CryptoConfig.MapNameToOID("SHA256"));
            string signedResult = Encoding.Default.GetString(signedRSA);

            //Verify with public key 
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.FromXmlString(publicXml);
            bool SignatureOK = rsaPublic.VerifyData(toEncryptData, CryptoConfig.MapNameToOID("SHA256"), signedRSA);

            if (SignatureOK)
                Assert.Pass();
            else
                Assert.Fail();
        }

        //
        // ===== SPEED TESTS TO MAKE SURE IMPORT DOESNT EAT GENERATE CPU =====
        //
        [Test]
        public void RSASpeedPass()
        {
            int i;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            for (i = 0; i < 600; i++)
            {
                RSACryptoServiceProvider tmpKey = new RSACryptoServiceProvider(2048);
                tmpKey.FromXmlString(privateXml);
            }

            Assert.Pass();
        }
    }
}