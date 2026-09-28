using System.Linq;
using NUnit.Framework;

namespace Odin.Core.Cryptography.Tests
{
    /// <summary>
    /// A secret split into Shamir shards must come back at its original length (#1736). The split keeps only the
    /// secret's numeric value, so a secret starting with 0x00 -- about 1 distribution key in 256 -- used to
    /// reconstruct a byte short, and Shamir password recovery then failed every time for that owner.
    /// </summary>
    [TestFixture]
    public class TestShamirLeadingZero
    {
        private static byte[] Secret(params byte[] leading)
        {
            var secret = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            leading.CopyTo(secret, 0);
            return secret;
        }

        [TestCase(new byte[] { })]
        [TestCase(new byte[] { 0x00 })]
        [TestCase(new byte[] { 0x00, 0x00 })]
        public void ASecretIsReconstructedAtItsOriginalLength(byte[] leading)
        {
            var secret = Secret(leading);
            var shards = ShamirSecretSharing.GenerateShamirShares(5, 3, secret);

            var reconstructed = ShamirSecretSharing.ReconstructShamirSecret(shards.Take(3).ToList(), secret.Length);

            Assert.That(reconstructed, Is.EqualTo(secret));
        }
    }
}
