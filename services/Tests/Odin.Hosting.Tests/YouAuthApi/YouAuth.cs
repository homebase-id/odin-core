using System.Net;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;

namespace Odin.Hosting.Tests.YouAuthApi
{
    /// <summary>
    /// Test YouAuth login for Users and Apps
    /// See diagram ... insert URL "V2"
    /// </summary>
    public class TestYouAuthLogin
    {
        OdinId sam = new OdinId("samwisegamgee.me");
        OdinId frodo = new OdinId("frodobaggins.me");

        [SetUp]
        public void Setup()
        {
        }

        /// <summary>
        /// Step [010]
        /// Sam is on Frodo's site and wants to YouAuth authenticate.
        /// Test invalid requests give proper invalid responses
        /// </summary>
        [Test]
        public void TestStep010()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(ref key, 1);

            string currentUrl = $"https://{frodo.DomainName}/home?q=1";
            string authUrl = $"https://api.{frodo.DomainName}/YouAuth/auth?u=42&returnUrl={WebUtility.UrlEncode(currentUrl)}";

            // Build request missing the rsa key
            string redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?returnUrl={WebUtility.UrlEncode(authUrl)}";
            // Simulate the browser redirect here by calling HttpClient(redirectUrl);
            // The response from the server should be an error, missing rsa parameter


            // Build request missing the returnUrl
            redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?rsa={rsa.publicKey.ToBase64()}";
            // Simulate the browser redirect here by calling HttpClient(redirectUrl);
            // The response from the server should be an error, missing rsa parameter
        }


        /// <summary>
        /// Step [010] -> [012] test
        /// Sam is on Frodo's site and wants to YouAuth authenticate
        /// Sam isn't logged into his own site, so he'll get a browser redirect to his own owner login (Step 012)
        /// </summary>
        [Test]
        public void TestStep010and012()
        {
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(ref key, 1);

            string currentUrl = $"https://{frodo.DomainName}/home?q=1";
            string authUrl = $"https://api.{frodo.DomainName}/YouAuth/auth?u=42&returnUrl={WebUtility.UrlEncode(currentUrl)}";
            string redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?rsa={rsa.publicKey.ToBase64()}&returnUrl={WebUtility.UrlEncode(authUrl)}";

            // Simulate the browser redirect here by calling HttpClient(redirectUrl);
            // The response from the server should be a redirect to Sam's owner login, step 012
        }


        // ***************************
        //         The Husker
        // ***************************
        // Add TestStep010and014, App 
        // ***************************


        /// <summary>
        /// Step [010] -> [015] test
        /// Sam tries to YouAuth authenticate on his own site
        /// Not allowed so we get a 403 Forbidden error back (Step 015)
        /// </summary>
        [Test]
        public void TestStep010and015()
        {
            // Log Sam in as Owner so we don't end up in step 012 (need Todd's help?)
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(ref key, 1);

            string currentUrl = $"https://{frodo.DomainName}/home?q=1";
            string authUrl = $"https://api.{frodo.DomainName}/YouAuth/auth?u=42&returnUrl={WebUtility.UrlEncode(currentUrl)}";
            string redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?rsa={rsa.publicKey.ToBase64()}&returnUrl={WebUtility.UrlEncode(authUrl)}";

            // 403 Forbidden
        }


        /// <summary>
        /// Step [010] -> [016] test
        /// Sam tries to YouAuth authenticate on Frodo's site. 
        /// Frodo and Sam are not connected and Sam hasn't set Frodo as "OK" either.
        /// Result is therefore a redirect to Sam's browser to OK the YouAuth request (Step 016)
        /// </summary>
        [Test]
        public void TestStep010and016A()
        {
            // Log Sam in as Owner so we don't end up in step 012 (need Todd's help?)
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(ref key, 1);

            string currentUrl = $"https://{frodo.DomainName}/home?q=1";
            string authUrl = $"https://api.{frodo.DomainName}/YouAuth/auth?u=42&returnUrl={WebUtility.UrlEncode(currentUrl)}";
            string redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?rsa={rsa.publicKey.ToBase64()}&returnUrl={WebUtility.UrlEncode(authUrl)}";

            // We should get a browser redirect to Sam's dialogue to OK YouAuth request
        }


        /// <summary>
        /// Step [010] -> [016] test
        /// Sam tries to YouAuth authenticate on Frodo's site. 
        /// Frodo and Sam ARE connected
        /// Result is therefore the final redirect 020
        /// </summary>
        [Test]
        public void TestStep010and016B()
        {
            // Log Sam in as Owner so we don't end up in step 012 (need Todd's help?)
            // Make Sam and Frodo connected
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(ref key, 1);

            string currentUrl = $"https://{frodo.DomainName}/home?q=1";
            string authUrl = $"https://api.{frodo.DomainName}/YouAuth/auth?u=42&returnUrl={WebUtility.UrlEncode(currentUrl)}";
            string redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?rsa={rsa.publicKey.ToBase64()}&returnUrl={WebUtility.UrlEncode(authUrl)}";

            // We should get a browser redirect to Sam's dialogue to OK YouAuth request
            // Here we can extract the token from the QS, decrypt it with this code.
            // And execute Step [032] and validate it all the way till the end
            /*
            byte[] data = { 1, 2, 3, 4, 5 };

            var cipher = rsa.Encrypt(data); // Encrypt with public key 
            var decrypt = rsa.Decrypt(ref key, cipher); // Decrypt with private key

            if (ByteArrayUtil.EquiByteArrayCompare(data, decrypt) == false)
                Assert.Fail();
            else
                Assert.Pass();*/
        }


        /// <summary>
        /// Step [010] -> [016] test
        /// Sam tries to YouAuth authenticate on Frodo's site. 
        /// Frodo and Sam are NOT connected, but Sam has previously OK'd Frodo with a "remember" checkmark
        /// Result is therefore the final redirect 020
        /// </summary>
        [Test]
        public void TestStep010and016C()
        {
            // Log Sam in as Owner so we don't end up in step 012 (need Todd's help?)
            // Make Sam's OK decision remembered
            // Maybe don't make this test .... ?
            var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            var rsa = new RsaFullKeyData(ref key, 1);

            string currentUrl = $"https://{frodo.DomainName}/home?q=1";
            string authUrl = $"https://api.{frodo.DomainName}/YouAuth/auth?u=42&returnUrl={WebUtility.UrlEncode(currentUrl)}";
            string redirectUrl = $"https://api.{sam.DomainName}/YouAuth/step010?rsa={rsa.publicKey.ToBase64()}&returnUrl={WebUtility.UrlEncode(authUrl)}";

            // We should move on to step [020], just validate the redirect, but no need to repeat the test from B (decrypt etc).
        }

    }
}
