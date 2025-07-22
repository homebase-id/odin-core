using System;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests
{
    class TestEmailLinkHelper
    {
        [Test]
        public void EmailLinkHelperPass()
        {
            try
            {
                var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

                var (token, cipher) = EmailLinkHelper.SplitSecret(secret);

                var recovered = EmailLinkHelper.AssembleSecret(token, cipher);

                if (!recovered.SequenceEqual(secret))
                    Assert.Fail("Secret recovery failed.");

                // Optional: Test URL build and parse
                Guid id = Guid.NewGuid();
                string baseUrl = "https://example.com/reset";
                string resetUrl = EmailLinkHelper.BuildResetUrl(baseUrl, id, token);

                var (parsedRowId, parsedToken) = EmailLinkHelper.ParseResetUrl(resetUrl);

                if (parsedRowId != id || parsedToken != token)
                    Assert.Fail("URL build/parse roundtrip failed.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                Assert.Fail();
            }
        }
    }
}