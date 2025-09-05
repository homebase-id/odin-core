using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests
{
    public class TestShamirSecretSharing : IocTestBase
    {
        private const string Dealer = "frodo.me";
        private readonly OdinId DealerOdinId = new OdinId(Dealer);

        // Dealer is the person who is creating shards from his password and distributing them
        // Players are the people / machines receiving said shards

        private Dictionary<Guid, object> dealerDisk = new Dictionary<Guid, object>();
        private Dictionary<Guid, object> playerDisk = new Dictionary<Guid, object>();

        private byte[] dealerRandomKey = ByteArrayUtil.GetRndByteArray(16);
        private byte[] playerOwnerKey = ByteArrayUtil.GetRndByteArray(16);
        private byte[] playerPeerKey = ByteArrayUtil.GetRndByteArray(16);


        // Move this enum and the record below to Todd's code or ShamirsSecret
        public enum ShardType
        {
            Automatic = 1,   // The dealer can request the copy of the (encrypted) shard from a (machine) player
            Delegate = 2,    // The (human) player must click OK to release a shard to the dealer.
            Manual = 3       // The (human) player must give the information out-of-band, Not yet implemented
        }

        // This record represents a player (ends up as a list of players)
        public record ShamirPlayer(int Index, ShardType Type, OdinId player);

        // This is the data structured stored at a player.
        // Delegate players encrypt the EncryptedShard with their master-key and it thus requires they
        // login to the owner console (where they first decrypt with their master-key, then with the dealer's key)
        // They get the dealer's key from the dealer's server over Peer, but can only get it when it is in recovery mode
        public record ShamirShardPlayerWrapper(ShamirPlayer Player, Guid Id, byte[] DealerEncryptedShard);

        public record ShamirShardPlayerDoubleWrapper(OdinId Dealer, ShamirPlayer Player, Guid Id, byte[] DoubleEncryptedShard, byte[] PlayerIv);

        // One each these data structures are stored with the dealer for each shard given to a player
        // The key is encrypted with the drive key, and can thus be decrypted by a player when they request it.
        // (They can only request it when dealer's host is in recovery mode)
        public record ShamirShardDealerWrapper(Guid Id, ShamirPlayer Player, byte[] Key, byte[] Iv);


        // A dealer sends a shard / playerRecord to a player
        private bool SendOverPeerD2PSendShard(ShamirShardPlayerWrapper playerShardRecord)
        {
            var targetHost = playerShardRecord.Player;
            // Now send the playerRecord over the Peer API to targetHost.
            // Peer will ensure it is doubly encrypted with HTTPS and Peer encryption
            // and will also use X.509 certificates to ensure both sender and receiver domains

            return PeerPlayerReceiveShard(DealerOdinId, playerShardRecord); // this places the https call over peer
        }

        // Player receives an incoming request over peer
        private bool PeerPlayerReceiveShard(OdinId dealer, ShamirShardPlayerWrapper playerRecord)
        {
            // ClassicAssert.IsTrue(playerRecord.Player == this identity odin Id);
            // Would be nice to check that we don't already have another shard from the dealer - 
            // if we do then we should clean it up or deny. If we store them as files on a drive
            // the index could have the OdinId(Hash) so we can query by it.

            // Store the record
            if (playerRecord.Player.Type == ShardType.Automatic)
            {
                // Doesn't have to be an identity

                // Make sure my identity is s0-s2.homebase.id or your own configured.
                // Meaning they are in a configuration file

                // No player encryption for automatic?

                // Maybe this is not possible ... :
                // Retrieve the drive key for the rescue drive, below we simulate it
                // The drive key is unavailable when the server is at rest and is only available
                // when the player in question makes a PEER API request.
                //
                var driveKeyba = playerPeerKey;
                var driveKeysba = new SensitiveByteArray(driveKeyba);

                // Now we doubly encrypt the dealer's encrypted shard with a player's 
                // 
                var (playerIv, playerDealerEncryptedShard) = AesCbc.Encrypt(playerRecord.DealerEncryptedShard, driveKeysba);
                var recordToStore = new ShamirShardPlayerDoubleWrapper(dealer, playerRecord.Player, playerRecord.Id, playerDealerEncryptedShard, playerIv);

                SaveToPlayerDisk(recordToStore);
            }
            else
            {
                // Need to support direct write to shakira drive
                // Encryption key preferably is not obtainable by dealer over peer
                // Dealer only has write access
                //
                // Retrieve an owner console only key, below we simulate it
                // The player must be in the owner console to decrypt the shard
                var ownerKeyba = playerOwnerKey;
                var ownerKeysba = new SensitiveByteArray(ownerKeyba);

                // Now we doubly encrypt the dealer's encrypted shard with the player's key
                // We ensure that if someone steals both the dealer and player's server, they 
                // still cannot derive the password
                var (playerIv, playerDealerEncryptedShard) = AesCbc.Encrypt(playerRecord.DealerEncryptedShard, ownerKeysba);
                var recordToStore = 
                    new ShamirShardPlayerDoubleWrapper(dealer, playerRecord.Player, playerRecord.Id, playerDealerEncryptedShard, playerIv);

                SaveToPlayerDisk(recordToStore);
            }

            return true;
        }

        // A player saves the package to disk
        private void SaveToPlayerDisk(ShamirShardPlayerDoubleWrapper record)
        {
            playerDisk.Add(record.Id, record);
        }

        // A dealer saves the package to disk
        private void SaveToDealerDisk(ShamirShardDealerWrapper record)
        {
            dealerDisk.Add(record.Id, record);
        }


        // A player loads the Id from the disk
        private ShamirShardPlayerDoubleWrapper LoadFromPlayerDisk(Guid Id)
        {
            if (playerDisk.ContainsKey(Id))
                return (ShamirShardPlayerDoubleWrapper) playerDisk[Id];
            else
                return null;
        }

        // A dealer loads the Id from the disk
        private ShamirShardDealerWrapper LoadFromDealerDisk(Guid Id)
        {
            if (dealerDisk.ContainsKey(Id))
                return (ShamirShardDealerWrapper)dealerDisk[Id];
            else
                return null;
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

        // Dealer sends signal to Player that they need their Delegate help
        private void PeerRequestDelegateShard(OdinId dealer, Guid Id)
        {
            if (PeerDealerIsInRecovery(dealer) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // We load it from the disk
            var r = LoadFromPlayerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Dealer != dealer)
                throw new Exception("Dealer mismatch");

            if (r.Player.Type != ShardType.Delegate)
                throw new Exception("Shard type is not set to Delegate");

            // Notify the player that Dealer needs their help
        }


        // An API the player Delegate app can call to reveal the shard
        // This happens when the player is in the owner console and activates
        // the recovery sequence to help his friend. Once approved by the player
        // the dealer's host will have the shard
        // The return type is just for the TEST - IRL it should not have a return type
        private byte[] LocalApiResolveDelegateShard(Guid Id)
        {
            // The player loads the shard from the disk
            var r = LoadFromPlayerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Player.Type != ShardType.Delegate)
                throw new Exception("Shard type is not set to automatic");

            if (PeerDealerIsInRecovery(r.Dealer) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // Get the player's decryption key, some kind of owner key
            var ownerKeyba = playerOwnerKey;
            var ownerKeysba = new SensitiveByteArray(ownerKeyba);

            // Now we decrypt the doubly encrypted shard
            var dealerEncryptedShard = AesCbc.Decrypt(r.DoubleEncryptedShard, ownerKeyba, r.PlayerIv);

            // Now we call the Dealer so he can decrypt it
            // IRL this should return OK if successful
            var decryptedShard = PeerPlayerDecryptDelegateShard(r.Player.player, Id, dealerEncryptedShard);

            // Should return OK if the player successfully delivered the shard
            return decryptedShard;
        }


        // A Peer API. The Player calls the Dealer requesting to have the shard
        // revealed. 
        private byte[] PeerPlayerDecryptDelegateShard(OdinId player, Guid Id, byte[] Cipher)
        {
            // We load it from the disk
            var r = LoadFromDealerDisk(Id);

            if (r == null)
                throw new Exception("No such record found on disk");

            if (r.Player.Type != ShardType.Delegate)
                throw new Exception("Shard type is not set to automatic");

            // Assert r.Dealer == this.OdinId

            // Call to self
            if (PeerDealerIsInRecovery(DealerOdinId) == false)
                throw new Exception("Dealer must be in password recovery mode");

            // Get the owners's decryption key, some kind of owner key
            var ownerKeyba = dealerRandomKey;
            var ownerKeysba = new SensitiveByteArray(ownerKeyba);

            // Now we decrypt the doubly encrypted shard
            var decryptedShard = AesCbc.Decrypt(Cipher, r.Key, r.Iv);

            // The dealer API should just return OK to the player if successfully decrypted
            // But we return it here to make the TEST easier
            return decryptedShard;
        }



        // The dealer requests an automatic shard from a player
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
            var driveKeyba = playerPeerKey;
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
        /// In this example we encrypt our secret (a random password encryption key)
        /// Then we encrypt it with a random key for each recipient, so they cannot decrypt them until the 
        /// dealer server is in "password recovery mode".
        /// Then we give it to three automatic and two manual (people)
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task ShamirSecretSharingFlowSetupExample(DatabaseType databaseType)
        {
            // A random encryption key that will encrypt the dealer's password
            var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            int totalShards = 5;
            int minShards = 4; // Default setup is 3:3

            // First we split dealer Frodo's secret into 5 parts where 4 players can reconstruct Frodo's password
            var shards = ShamirSecretSharing.GenerateShamirShares(totalShards, minShards, secret);

            // This is how Frodo has setup his 5 shards.
            // s0-s2 are run by Homebase and can be requested by Frodo's server
            // Sam and Gandalf are humanoids and Frodo must call, text or otherwise contact them to get his share
            // Sam and Gandalf are connected to Frodo (delegates must be connected)
            // The default setup is s0-s2
            var shamirPlayers = new List<ShamirPlayer>()
            {
                new ShamirPlayer(shards[0].Index, ShardType.Automatic, new OdinId("s0.homebase.id")),
                new ShamirPlayer(shards[1].Index, ShardType.Automatic, new OdinId("s1.homebase.id")),
                new ShamirPlayer(shards[2].Index, ShardType.Automatic, new OdinId("s2.homebase.id")),
                new ShamirPlayer(shards[3].Index, ShardType.Delegate, new OdinId("samwise.me")),
                new ShamirPlayer(shards[4].Index, ShardType.Delegate, new OdinId("gandalf.me"))
            };

            // These are the objects stored with each player.
            var shamirShardPlayerWrappers = new List<ShamirShardPlayerWrapper>();
            var shamirShardDealerWrappers = new List<ShamirShardDealerWrapper>();

            ClassicAssert.IsTrue(shards.Count == 5);
            ClassicAssert.IsTrue(shamirPlayers.Count == 5);

            for (int i = 0; i < shards.Count; i++)
            {
                // The dealer generates a random encryption key (that he will store) for each player / shard
                var keyba = ByteArrayUtil.GetRandomCryptoGuid().ToByteArray();
                var keysba = new SensitiveByteArray(keyba);

                ClassicAssert.IsTrue(shards[i].Index == i + 1);
                // Deaaler encrypts a player's shard with the random key
                var (Iv, CipherText) = AesCbc.Encrypt(shards[i].Shard, keysba);
                var guidId = Guid.NewGuid();

                // We create the player record that we will send to the player
                var playerRecord = new ShamirShardPlayerWrapper(shamirPlayers[i], guidId, CipherText);
                shamirShardPlayerWrappers.Add(playerRecord);
                
                // This record is stored on the dealer's host, for each player
                var dealerRecord = new ShamirShardDealerWrapper(guidId, playerRecord.Player, keyba, Iv);
                SaveToDealerDisk(dealerRecord);
                shamirShardDealerWrappers.Add(dealerRecord);

                if (SendOverPeerD2PSendShard(playerRecord) == false)
                {
                    throw new Exception("Some communication failed... we should probably cleanup the shards that got delivered");
                    // Or maybe this can happen over the outbox?
                }
            }

            // Now we have stored five objects locally and five with each player

            // The dealer forgets his password and initiates the recovery mode as shown in
            // [TEST] ShamirSecretSharingFlowRecoveryExample()

            // .. Now we're in recovery mode. Let's get the automatic shards.
            var peerShard1 = PeerRequestAutomaticShard(DealerOdinId, shamirShardPlayerWrappers[0].Id);
            var ddisk1 = LoadFromDealerDisk(shamirShardPlayerWrappers[0].Id);
            var dShard1 = AesCbc.Decrypt(peerShard1.DealerEncryptedShard, ddisk1.Key, ddisk1.Iv);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(dShard1, shards[0].Shard) == true);
            var shard1 = new ShamirSecretSharing.ShamirShard(ddisk1.Player.Index, dShard1);
            
            var peerShard2 = PeerRequestAutomaticShard(DealerOdinId, shamirShardPlayerWrappers[1].Id);
            var ddisk2 = LoadFromDealerDisk(shamirShardPlayerWrappers[1].Id);
            var dShard2 = AesCbc.Decrypt(peerShard2.DealerEncryptedShard, ddisk2.Key, ddisk2.Iv);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(dShard2, shards[1].Shard) == true);
            var shard2 = new ShamirSecretSharing.ShamirShard(ddisk2.Player.Index, dShard2);
            
            var peerShard3 = PeerRequestAutomaticShard(DealerOdinId, shamirShardPlayerWrappers[2].Id);
            var ddisk3 = LoadFromDealerDisk(shamirShardPlayerWrappers[2].Id);
            var dShard3 = AesCbc.Decrypt(peerShard3.DealerEncryptedShard, ddisk3.Key, ddisk3.Iv);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(dShard3, shards[2].Shard) == true);
            var shard3 = new ShamirSecretSharing.ShamirShard(ddisk3.Player.Index, dShard3);

            // Signal the player that we need their help
            var ddisk4 = LoadFromDealerDisk(shamirShardDealerWrappers[3].Id);
            PeerRequestDelegateShard(DealerOdinId, ddisk4.Id);
            // ... 3 hours later ... the player does it
            var dShard4 = LocalApiResolveDelegateShard(ddisk4.Id);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(dShard4, shards[3].Shard) == true);
            var shard4 = new ShamirSecretSharing.ShamirShard(ddisk4.Player.Index, dShard4);
            
            // Signal the player that we need their help
            var ddisk5 = LoadFromDealerDisk(shamirShardDealerWrappers[4].Id);
            PeerRequestDelegateShard(DealerOdinId, ddisk5.Id);
            // ... 3 hours later ... the player does it
            var dShard5 = LocalApiResolveDelegateShard(ddisk5.Id);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(dShard5, shards[4].Shard) == true);
            var shard5 = new ShamirSecretSharing.ShamirShard(ddisk5.Player.Index, dShard5);

            // Now we have the four (five) shards, we can use Shamir to reconstruct the secret
            var reconstructed = ShamirSecretSharing.ReconstructShamirSecret([shard1, shard2, shard3, shard4]);

            if (!reconstructed.SequenceEqual(secret))
                Assert.Fail("Reconstruction with min shards failed.");

            // ==========================================================================

            // Now we have the secret, we build and email with a reset link and send it to the dealer
            // We don't want to store the secret on his server
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tableNonce = scope.Resolve<TableNonce>();

            // Split the secret
            var (xorkey, randomkey) = XorManagement.XorSplitKey(secret);

            // Generate a Nonce with the xorkey
            var nonceId = Guid.NewGuid();
            var r = new NonceRecord()
            {
                id = nonceId,
                expiration = UnixTimeUtc.Now().AddHours(24*7), // One week expiration
                data = xorkey.ToBase64()
            };
            var n = await tableNonce.InsertAsync(r);
            ClassicAssert.IsTrue(n == 1);

            // Generate the email link with the other half key
            var l = EmailLinkHelper.BuildResetUrl("https://frodobaggins.me", nonceId, randomkey.ToBase64());

            // Send the email
            r = null;

            // Now the user clicks the link, at the server we parse the URL
            //
            var (id, Token) = EmailLinkHelper.ParseResetUrl(l);

            // Then we get the corresponding NONCE from the DB
            r = await tableNonce.PopAsync(id);

            // Now we should be able to reconstruct the sercet used to encrypt the user's password
            var reconstructedSecret = XorManagement.XorDecrypt(Convert.FromBase64String(r.data), Convert.FromBase64String(Token));
            if (!reconstructedSecret.SequenceEqual(secret))
                Assert.Fail("Reconstruction from email magic link  failed.");

            // We can now reset the password

            // The nonce is great and all, but what if the user clicks the link and messes up somehow
            // and doesn't complete. Maybe we insert it back into the table after popping?
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task ShamirSecretSharingFlowRecoveryExample(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tableNonce = scope.Resolve<TableNonce>();

            var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            //var email = "frodo@baggins.me";
            //int totalShards = 5;
            //int minShards = 4;

            // ============================================================================ 
            // The user clicks on "forgot password via email"
            // He will receive an email that tells him to click the link to initiate password recovery
            // ============================================================================ 

            var nonceId = Guid.NewGuid();
            var r = new NonceRecord()
            {
                id = nonceId,
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