using NUnit.Framework;

namespace Odin.Core.Cryptography.Tests
{
    public class TestHostToHostManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        //
        // ===== HOST TO HOST PACKET TESTS -- REDO THIS CODE & HOST TO HOST =====
        //
        [Test]
        public void HostToHostPacketPass()
        {
            /*
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            string hdr = "-----BEGIN RSA PRIVATE KEY-----\n";
            string ftr = "\n-----END RSA PRIVATE KEY-----";
            string priv = Convert.ToBase64String(rsaGenKeys.ExportPkcs8PrivateKey());
            string pem = hdr + priv + ftr;

            // Data to encrypt
            string mySecret = "hello wørld";
            byte[] payload = Encoding.UTF8.GetBytes(mySecret);

            var (rsaHeader, encryptedPayload) = HostToHostManager.EncryptRSAPacket(payload, publicXml);

            // Now imagine we're at the recipient host:
            var copyPayload = HostToHostManager.DecryptRSAPacket(rsaHeader, encryptedPayload, privateXml);

            string copySecret = Encoding.UTF8.GetString(copyPayload);

            if (copySecret == mySecret)
                Assert.Pass();
            else
                Assert.Fail();*/
        }



        /// <summary>
        /// This test illustrates how to take a host to host package, with a RSA header,
        /// and then transform the RSA header into the AES header (for local storage).
        /// </summary>
        [Test]
        public void HostToHostPacketHeaderTransformPass()
        {
            /*
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello wørld";
            byte[] payload = Encoding.UTF8.GetBytes(mySecret);

            var (rsaHeader, encryptedPayload) = HostToHostManager.EncryptRSAPacket(payload, publicXml);

            var sharedSecret = YFByteArray.GetRndByteArray(16);
            var aesHeader = HostToHostManager.TransformRSAtoAES(rsaHeader, privateXml, sharedSecret);
            // var (iv, keyEncrypted) = HostToHost.TransformRSAtoAES(rsaHeader, privateXml, sharedSecret);

            // Now let's see if we can decode the header
            var (randomIv2, encryptedUnlockHeader) = HostToHostManager.ParseAesHeader(aesHeader);
            var unlockHeader = AesCbc.DecryptBytesFromBytes_Aes(encryptedUnlockHeader, sharedSecret, randomIv2);
            var (key, iv) = HostToHostManager.ParseUnlockHeader(unlockHeader);
            var data = AesCbc.DecryptBytesFromBytes_Aes(encryptedPayload, key, iv);

            string originalResult = Encoding.UTF8.GetString(data);

            if (originalResult == mySecret)
                Assert.Pass();
            else
                Assert.Fail();*/
        }
    }
}