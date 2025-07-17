using Autofac;
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
        public record ShamirShardPlayerWrapper(ShamirPlayer Player, Guid Id, byte[] DealerEncryptedShard);

        public record ShamirShardPlayerDoubleWrapper(OdinId Dealer, ShamirPlayer Player, Guid Id, byte[] DoubleEncryptedShard, byte[] PlayerIv);

        // One each these data structures are stored with the dealer for each shard given to a player
        // The key is encrypted with the drive key, and can thus be decrypted by a player when they request it.
        // (They can only request it when dealer's host is in recovery mode)
        public record ShamirShardDealerWrapper(Guid Id, int Index, ShardType Type, byte[] Key, byte[] Iv);


        // A dealer sends a shard / playerRecord to a player
        private bool SendOverPeerD2PSendShard(ShamirShardPlayerWrapper playerShardRecord)
        {
            var targetHost = playerShardRecord.Player;
            // Now send the playerRecord over the Peer API to targetHost.
            // Peer will ensure it is doubly encrypted with HTTPS and Peer encryption
            // and will also use X.509 certificates to ensure both sender and receiver domains

            return PeerPlayerReceiveShard(new OdinId("frodo.me"), playerShardRecord); // this places the https call over peer
        }

        // Player receives an incoming request over peer
        private bool PeerPlayerReceiveShard(OdinId dealer, ShamirShardPlayerWrapper playerRecord)
        {
            // ClassicAssert.IsTrue(playerRecord.Player == this identity odin Id);
            // Would be nice to check that we don't already have another shard from the dealer - 
            // if we do then we should clean it up or deny. If we store them as files on a drive
            // the index could have the OdinId(Hash) so we can query by it.

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
                var recordToStore = new ShamirShardPlayerDoubleWrapper(dealer, playerRecord.Player, playerRecord.Id, playerDealerEncryptedShard, playerIv);

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
                    new ShamirShardPlayerDoubleWrapper(dealer, playerRecord.Player, playerRecord.Id, playerDealerEncryptedShard, playerIv);

                // We store "recordToStore" on the recovery drive probably with owner ACL only
            }

            return true;
        }

        // A player loads the Id from the disk
        private ShamirShardPlayerDoubleWrapper LoadFromPlayerDisk(Guid Id)
        {
            // load it from disk, this is just siulating it
            var p = new ShamirPlayer(1, ShardType.Interactive, new OdinId("player.id"));
            var r = new ShamirShardPlayerDoubleWrapper(new OdinId("dealer.id"), p, Id, null, null);

            return r;
        }

        // A dealer loads the Id from the disk
        private ShamirShardDealerWrapper LoadFromDealerDisk(Guid Id)
        {
            // load it from disk, this is just siulating it
            var r = new ShamirShardDealerWrapper(Id, 1, ShardType.Interactive, null, null);

            return r;
        }


        // Dealer calls player over peer and asks to have the shard removed
        private void PeerRequestRemoveShard(OdinId dealer, Guid Id)
        {
            // We load it from the disk
            var r = LoadFromPlayerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Dealer != dealer)
                throw new Exception("Dealer mismatch");

            // Hard delete it from the disk
        }

        // Dealer sends signal to Player that they need their interactive help
        private void PeerRequestInteractiveShard(OdinId dealer, Guid Id)
        {
            if (PeerDealerIsInRecovery(dealer) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // We load it from the disk
            var r = LoadFromPlayerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Dealer != dealer)
                throw new Exception("Dealer mismatch");

            if (r.Player.Type != ShardType.Automatic)
                throw new Exception("Shard type is not set to automatic");

            // Notify the player that Dealer needs their help
        }


        // An API the player interactive app can call to reveal the shard
        // This happens when the player is in the owner console and activates
        // the recovery sequence to help his friend
        private string LocalApiResolveInteractiveShard(Guid Id)
        {
            // We load it from the disk
            var r = LoadFromPlayerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Player.Type != ShardType.Interactive)
                throw new Exception("Shard type is not set to automatic");

            if (PeerDealerIsInRecovery(r.Dealer) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // Get the player's decryption key, some kind of owner key
            var ownerKeyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
            var ownerKeysba = new SensitiveByteArray(ownerKeyba);

            // Now we decrypt the doubly encrypted shard
            var dealerEncryptedShard = AesCbc.Decrypt(r.DoubleEncryptedShard, ownerKeyba, r.PlayerIv);

            // Now we call the Dealer and get it decrypted
            var visualResult = PeerDecryptInteractiveShard(r.Player.player, Id, dealerEncryptedShard);

            // We return something human readable - base64? bip39?
            return visualResult;
        }


        // A Peer API. The Player calls the Dealer requesting to have the shard
        // revealed. 
        private string PeerDecryptInteractiveShard(OdinId player, Guid Id, byte[] Cipher)
        {
            // We load it from the disk
            var r = LoadFromDealerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Type != ShardType.Interactive)
                throw new Exception("Shard type is not set to automatic");

            // Assert r.Dealer == this.OdinId

            // Call to self
            if (PeerDealerIsInRecovery(new OdinId("dealer.id")) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // Get the owners's decryption key, some kind of owner key
            var ownerKeyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
            var ownerKeysba = new SensitiveByteArray(ownerKeyba);

            // Now we decrypt the doubly encrypted shard
            var decryptedShard = AesCbc.Decrypt(Cipher, r.Key, r.Iv);

            // We return the shard to the dealer which is encrypted by the dealer
            return Convert.ToBase64String(decryptedShard);
        }




        private ShamirShardPlayerWrapper PeerRequestAutomaticShard(OdinId dealer, Guid Id)
        {
            if (PeerDealerIsInRecovery(dealer) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // We load it from the disk
            var r = LoadFromPlayerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Dealer != dealer)
                throw new Exception("Dealer mismatch");

            if (r.Player.Type != ShardType.Automatic)
                throw new Exception("Shard type is not set to automatic");

            // Get the player's decryption key
            var driveKeyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
            var driveKeysba = new SensitiveByteArray(driveKeyba);


            // Now we decrypt the doubly encrypted shard
            var dealerEncryptedShard = AesCbc.Decrypt(r.DoubleEncryptedShard, driveKeyba, r.PlayerIv);
            var recordToReturn = new ShamirShardPlayerWrapper(r.Player, r.Id, dealerEncryptedShard);

            // We return the shard to the dealer which is encrypted by the dealer
            return recordToReturn;
        }

        // this is the dealer API that let's a player check if the dealer is in recovery mode
        private bool PeerDealerIsInRecovery(OdinId dealer)
        {
            // Load something and figure out if the owner initiated a password recovery
            // return the result

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
                var id = Guid.NewGuid();
                var playerRecord = 
                    new ShamirShardPlayerWrapper(new ShamirPlayer(shards[i].Index, ShardType.Automatic, shamirPlayers[i].player), 
                            id, CipherText);
                shamirShardPlayerWrapper.Add(playerRecord);

                var dealerRecord = new ShamirShardDealerWrapper(id, shards[i].Index, shamirPlayers[i].Type, keyba, Iv);
                shamirShardDealerWrapper.Add(dealerRecord);
                // Now we store the dealerRecords on the dealer's drive

                if (SendOverPeerD2PSendShard(playerRecord) == false)
                {
                    throw new Exception("Some communication failed... we should probably cleanup the shards that got delivered");
                    // Or maybe this can happen over the outbox?
                }
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