using Autofac;
using Docker.DotNet.Models;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TestShamirSecretSharing : IocTestBase
    {
        // Dealer is the person who is creating shards from his password and distributing them
        // Players are the people / machines receiving said shards

        // Move this enum and the record below to Todd's code or ShamirsSecret
        public enum ShardType
        {
            Automatic = 1,   // The dealer can request the copy of the (encrypted) shard
            Interactive = 2  // The player must tell or send the dealer the shard, it won't be sent via Homebase
        }

        // This record represents a player (ends up as a list of players)
        public record ShamirPlayer(int Index, ShardType Type, OdinId player);

        // This is the data structured stored at a player.
        // Interactive players encrypt the EncryptedShard with their master-key and it thus requires they
        // login to the owner console (where they first decrypt with their master-key, then with the dealer's key)
        // They get the dealer's key from the dealer's server over Peer, but can only get it when it is in recovery mode
        public record ShamirShardPlayerWrapper(ShamirPlayer Player, byte[] DealerEncryptedShard, byte[] DealerIv);

        public record ShamirShardPlayerDoubleWrapper(OdinId Dealer, ShamirPlayer Player, byte[] DoubleEncryptedShard, byte[] DealerIv, byte[] PlayerIv);

        // One each these data structures are stored with the dealer for each shard given to a player
        // The key is encrypted with the drive key, and can thus be decrypted by a player when they request it.
        // (They can only request it when dealer's host is in recovery mode)
        public record ShamirShardDealerWrapper(int Index, ShardType Type, byte[] Key, byte[] Iv);

        private bool SendOverPeerD2PSendShard(ShamirShardPlayerWrapper playerRecord)
        {
            var targetHost = playerRecord.Player;
            // Now send the playerRecord over the Peer API to targetHost.
            // Peer will ensure it is doubly encrypted with HTTPS and Peer encryption
            // and will also use X.509 certificates to ensure both sender and receiver domains

            return SendOverPeerPlayerReceiveShard(new OdinId("frodo.me"), playerRecord); // this places the https call over peer
        }

        // Player receives an incoming request over peer
        private bool SendOverPeerPlayerReceiveShard(OdinId sender, ShamirShardPlayerWrapper playerRecord)
        {
            // ClassicAssert.IsTrue(playerRecord.Player == this identity odin Id);

            // Store the record
            if (playerRecord.Player.Type == ShardType.Interactive)
            {
                // Retrieve the drive key for the rescue drive, below we simulate it
                // The drive key is unavailable when the server is at rest and is only available
                // when the player in question makes a PEER API request.
                //
                var driveKeyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
                var driveKeysba = new SensitiveByteArray(driveKeyba);

                // Now we doubly encrypt the dealer's encrypted shard with a player's 
                // 
                var (playerDealerEncryptedShard, playerIv) = AesCbc.Encrypt(playerRecord.DealerEncryptedShard, driveKeysba);
                var recordToStore = new ShamirShardPlayerDoubleWrapper(sender, playerRecord.Player, playerDealerEncryptedShard, playerRecord.DealerIv, playerIv);

                // We store "recordToStore" on the recovery drive probably with ACL { dealer } and thus
                // that allows us to read it when the dealer makes a request
            }
            else
            {
                // Retrieve an owner console only key, below we simulate it
                // The player must be in the owner console to decrypt the shard
                var ownerKeyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
                var ownerKeysba = new SensitiveByteArray(ownerKeyba);

                // Now we doubly encrypt the dealer's encrypted shard with the player's key
                // We ensure that if someone steals both the dealer and player's server, they 
                // still cannot derive the password
                var (playerDealerEncryptedShard, playerIv) = AesCbc.Encrypt(playerRecord.DealerEncryptedShard, ownerKeysba);
                var recordToStore = 
                    new ShamirShardPlayerDoubleWrapper(sender, playerRecord.Player, playerDealerEncryptedShard, playerRecord.DealerIv, playerIv);

                // We store "recordToStore" on the recovery drive probably with owner ACL only
            }

            return true;
        }

        /// <summary>
        /// Shows how to setup Shamir's secret sharing
        /// In this example we encrypt our secret (which one? the master key? or another key)
        /// Then we encrypt it somehow so the manual recipients cannot decrypt them until the 
        /// dealer server is in "password recovery mode".
        /// Then we give it to three automatic and two manual (people)
        /// </summary>
        [Test]
        public void ShamirSecretSharingFlowSetupExample()
        {
            var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            int totalShards = 5;
            int minShards = 4;

            // First we split Frodo's secret into 5 parts where 4 of them can reconstruct Frodo's password
            var shards = ShamirSecretSharing.GenerateShamirShares(totalShards, minShards, secret);

            // This is how Frodo has setup his 5 shards.
            // s0-s2 are run by Homebase and can be requested by Frodo's server
            // Sam and Gandalf are humanoids and Frodo must call, text or otherwise contact them to get his share
            var shamirPlayers = new List<ShamirPlayer>()
            {
                new ShamirPlayer(0, ShardType.Automatic, new OdinId("s0.homebase.id")),
                new ShamirPlayer(1, ShardType.Automatic, new OdinId("s1.homebase.id")),
                new ShamirPlayer(2, ShardType.Automatic, new OdinId("s2.homebase.id")),
                new ShamirPlayer(3, ShardType.Interactive, new OdinId("samwise.me")),
                new ShamirPlayer(4, ShardType.Interactive, new OdinId("gandalf.me"))
            };

            // These are the objects stored with each player.
            var shamirShardPlayerWrapper = new List<ShamirShardPlayerWrapper>();

            // These are the objects stored with the dealer.
            var shamirShardDealerWrapper = new List<ShamirShardDealerWrapper>();


            ClassicAssert.IsTrue(shards.Count == 5);
            ClassicAssert.IsTrue(shamirPlayers.Count == 5);

            for (int i = 0; i < shards.Count; i++)
            {
                ClassicAssert.IsTrue(shards[i].Index == i + 1);
                var keyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
                var keysba = new SensitiveByteArray(keyba);
                var (Iv, CipherText) = AesCbc.Encrypt(shards[i].Shard, keysba);
                var playerRecord = 
                    new ShamirShardPlayerWrapper(new ShamirPlayer(shards[i].Index, ShardType.Automatic, shamirPlayers[i].player), CipherText, Iv);
                shamirShardPlayerWrapper.Add(playerRecord);

                if (SendOverPeerD2PSendShard(playerRecord) == false)
                {
                    throw new Exception("Some communication failed... we should probably cleanup the shards that got delivered");
                    // Or maybe this can happen over the outbox?
                }

                var dealerRecord = new ShamirShardDealerWrapper(shards[i].Index, shamirPlayers[i].Type, keyba, Iv);
                // Now we store the dealerRecords on the dealer's drive
                shamirShardDealerWrapper.Add(dealerRecord);
            }
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task ShamirSecretSharingFlowRecoveryExample(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tableNonce = scope.Resolve<TableNonce>();

            var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var email = "frodo@baggins.me";
            int totalShards = 5;
            int minShards = 4;

            // ============================================================================ 
            // The user clicks on "forgot password via email"
            // He will receive an email that tells him to click the link to initiate password recovery
            // ============================================================================ 

            var nonceId = Guid.NewGuid();
            var r = new NonceRecord()
            {
                id = nonceId,
                identity = "frodo.baggins.me", // myself <--- is this needed?
                expiration = UnixTimeUtc.Now().AddHours(1), // 1 hour expiration
                data = ""
            };
            var n = await tableNonce.InsertAsync(r);
            ClassicAssert.IsTrue(n == 1);

            var l = EmailLinkHelper.BuildResetUrl("https://frodobaggins.me", nonceId, "");

            // Send the email to frodo@baggins.me

            // ============================================================================ 
            // Frodo clicks the link, we check the incoming link and if it's a match then we
            // put his host into password recovery mode
            // ============================================================================ 

            var (id, Token) = EmailLinkHelper.ParseResetUrl(l);

            // We pop the corresponding row from the Nonce table
            var r2 = await tableNonce.PopAsync(id);
            // if r2 is null then the email link is invalid and we do not put the system in recovery mode
            ClassicAssert.IsTrue(r2.id == id);

            // Now we put the server into password recovery mode, until the user has logged in again
            // When in password recovery mode, the API will now allow the manual players to fetch
            // decryption keys corresponding to their shards, and the automatic players are allowed to
            // deliver their encrypted shards so they can be decrypted locally by the dealer

            // ============================================================================ 
            // The Server must now automatically retrieve shards from automatic players
            // And notify manual players
            // ============================================================================ 



        }




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