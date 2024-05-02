using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Util;
using System.Collections.Concurrent;

namespace Odin.Core.Cryptography.Tests
{
    public class TestZandbox
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void TestBIPRecoveryKey()
        {
            byte[] key = Guid.NewGuid().ToByteArray();
            var s = BIP39Util.GenerateBIP39(key);
            var b = BIP39Util.DecodeBIP39(s);

            if (ByteArrayUtil.EquiByteArrayCompare(key, b.GetKey()))
                Assert.Pass();
            else
                Assert.Fail();
        }

        /// <summary>
        /// About 27 seconds to do 10,000 on MS' semi beast iii
        /// </summary>
        [Test]
        public void TestEccPerformance()
        {
            var pwd = new SensitiveByteArray(ByteArrayUtil.GetRandomCryptoGuid().ToByteArray());
            for (int i = 0; i < 10; i++)
            {
                var fk = new EccFullKeyData(pwd, EccKeySize.P384, 1);
            }
        }

            [Test]
        // Should split this into its own file.
        public void TestSymKeyAes()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(secret);

            var sk = key.DecryptKeyClone(secret);

            Assert.Pass();
        }

        [Test]
        // Should split this into its own file.
        public void TestSymKeyFailAes()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(secret);
            var garbage = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKeyClone(garbage);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }

        [Test]
        // Ensures that if the decrypted key is cached then we can't get a copy
        // later with a junk key.
        public void TestSymKeyAes2()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedAes(secret);

            var sk = key.DecryptKeyClone(secret);

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKeyClone(junk);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }


        [Test]
        // Should split this into its own file.
        public void TestSymKeyXor()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            // byte[] halfKey;
            var key = new SymmetricKeyEncryptedXor(secret, out var halfKey);
            var decryptKey = key.DecryptKeyClone(halfKey);

            if (ByteArrayUtil.EquiByteArrayCompare(decryptKey.GetKey(), secret.GetKey()))
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        // Should split this into its own file.
        public void TestSymKeyFailXor()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            // byte[] halfKey;
            var key = new SymmetricKeyEncryptedXor(secret, out var halfKey);

            var garbage = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                var decryptKey = key.DecryptKeyClone(garbage);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }

        [Test]
        // Ensures that if the decrypted key is cached then we can't get a copy
        // later with a junk key.
        public void TestSymKeyXor2()
        {
            var secret = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
            var key = new SymmetricKeyEncryptedXor(secret, out var halfKey);

            var sk = key.DecryptKeyClone(halfKey);

            var junk = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));

            try
            {
                key.DecryptKeyClone(junk);
                Assert.Fail();
            }
            catch
            {
                Assert.Pass();
            }
        }


        [Test]
        public void TestSha256BrainsPass()
        {
            // https://www.sciencedirect.com/topics/computer-science/encrypted-value
            var dataToEncrypt = Encoding.UTF8.GetBytes("brains");
            byte[] result = { 0x44, 0xde, 0x9b, 0x7b, 0x03, 0x6b, 0x9b, 0x8d, 0x28, 0xf3, 0x64, 0xfa, 0x36, 0x4b, 0x76, 0xb7, 0xaf, 0x64, 0xd9, 0xe0, 0xb9, 0xef, 0xe1, 0x7d, 0x75, 0x36, 0x03, 0x37, 0x72, 0xa0, 0x48, 0x71 };

            var r = ByteArrayUtil.CalculateSHA256Hash(dataToEncrypt);

            if (!ByteArrayUtil.EquiByteArrayCompare(result, r))
                Assert.Fail();

            Assert.Pass();

            //
            // I've manually verified that the JS counterpart reaches the same value
        }


        [TestFixture]
        public class AsyncLocalTests2
        {
            private static AsyncLocal<int> _asyncLocalId = new AsyncLocal<int>();
            private static int _nextId = 1;
            // Adding a ConcurrentDictionary similar to the scenario described
            private static ConcurrentDictionary<int, string> _connections = new ConcurrentDictionary<int, string>();

            [Test]
            public async Task TestAsyncLocalConsistencyAcrossAwaitsAndDictionaryUsage()
            {
                var task1 = RunTaskWithAsyncLocal(42);
                var task2 = RunTaskWithAsyncLocal(43);
                var task3 = RunTaskWithAsyncLocal(44);

                await Task.WhenAll(task1, task2, task3);

                // After all tasks complete, inspect the dictionary's state
                foreach (var kvp in _connections)
                {
                    Console.WriteLine($"Key (Task ID): {kvp.Key}, Value: {kvp.Value}");
                }

                Console.WriteLine($"Total connections: {_connections.Count}");
            }

            private async Task RunTaskWithAsyncLocal(int cheat)
            {
                // Unique ID for each task
                int taskId = Interlocked.Increment(ref _nextId);
                _asyncLocalId.Value = taskId;

                // Simulating adding a "connection" with the task ID as the key
                _connections.TryAdd(_asyncLocalId.Value, $"Connection for task {cheat}");

                Console.WriteLine($"{cheat} Before await, Task ID: {_asyncLocalId.Value}, Thread ID: {Thread.CurrentThread.ManagedThreadId}");

                // Simulate some asynchronous work
                await Task.Delay(TimeSpan.FromSeconds(1));

                // After await - should be the same ID, even if the thread ID might change
                Console.WriteLine($"{cheat} After await, Task ID: {_asyncLocalId.Value}, Thread ID: {Thread.CurrentThread.ManagedThreadId}");

                // Optionally, interact with the dictionary again after the await
                // to simulate checking or updating the connection status
                if (_connections.TryGetValue(_asyncLocalId.Value, out var value))
                {
                    Console.WriteLine($"Successfully retrieved {value} after await.");
                }
            }
        }



        [TestFixture]
        public class AsyncLocalTests
        {
            private static AsyncLocal<int> _asyncLocalId = new AsyncLocal<int>();
            private static int _nextId = 1;

            [Test]
            public async Task TestAsyncLocalConsistencyAcrossAwaits()
            {
                var task1 = RunTaskWithAsyncLocal(42);
                var task2 = RunTaskWithAsyncLocal(43);
                var task3 = RunTaskWithAsyncLocal(44);

                await Task.WhenAll(task1, task2, task3);
            }

            private async Task RunTaskWithAsyncLocal(int cheat)
            {
                // Unique ID for each task
                _asyncLocalId.Value = Interlocked.Increment(ref _nextId);

                Console.WriteLine($"{cheat} Before await, Task ID: {_asyncLocalId.Value}, Thread ID: {Thread.CurrentThread.ManagedThreadId}");

                // Simulate some asynchronous work
                await Task.Delay(TimeSpan.FromSeconds(1));

                // After await - should be the same ID, even if the thread ID might change
                Console.WriteLine($"{cheat} After await, Task ID: {_asyncLocalId.Value}, Thread ID: {Thread.CurrentThread.ManagedThreadId}");
            }
        }
    }
}
