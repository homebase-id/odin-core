using NUnit.Framework;
using System;
using System.Linq;

namespace Odin.Core.Cryptography.Tests
{
    public class TestShamirSecretSharing
    {
        [Test]
        public void ShamirSecretSharingPass()
        {
            try
            {
                var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

                int totalShards = 5;
                int minShards = 3;

                var shards = ShamirSecretSharing.GenerateShamirShares(totalShards, minShards, secret);

                // Reconstruct with minimum shares (should succeed)
                var reconShards = shards.Take(minShards).ToList();
                var reconstructed = ShamirSecretSharing.ReconstructShamirSecret(reconShards);

                if (!reconstructed.SequenceEqual(secret))
                    Assert.Fail("Reconstruction with min shards failed.");

                // Optional: Reconstruct with fewer than min (should fail to match original)
                var insufficientShares = shards.Take(minShards - 1).ToList();
                var badReconstructed = ShamirSecretSharing.ReconstructShamirSecret(insufficientShares);

                if (badReconstructed.SequenceEqual(secret))
                    Assert.Fail("Reconstruction with insufficient shards unexpectedly succeeded.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                Assert.Fail();
            }
        }
    }
}