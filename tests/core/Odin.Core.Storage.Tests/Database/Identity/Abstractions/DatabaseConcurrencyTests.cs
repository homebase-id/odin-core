using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions
{
    public class DatabaseConcurrencyTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyValue = scope.Resolve<TableKeyValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
            await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

            r = await tblKeyValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();
        }


        /// <summary>
        /// This test passes because the Database class locks on it's _transactionLock() object
        /// </summary>
        [Test, Explicit]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task WriteLockingTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);

            List<byte[]> Rows = new List<byte[]>();

            void writeDB1()
            {
                using var scope = Services.BeginLifetimeScope();
                var tblKeyValue = scope.Resolve<TableKeyValue>();
                for (int i = 0; i < 10000; i++)
                    tblKeyValue.UpdateAsync(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() }).Wait();
            }

            void writeDB2()
            {
                using var scope = Services.BeginLifetimeScope();
                var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();
                for (int i = 0; i < 10000; i++)
                    tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord()
                    { key1 = Rows[i], key2 = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() }).Wait();
            }

            void readDB()
            {
                using var scope = Services.BeginLifetimeScope();
                var tblKeyValue = scope.Resolve<TableKeyValue>();
                for (int i = 0; i < 10000; i++)
                    tblKeyValue.GetAsync(Rows[i]).Wait();
            }

            await using var scope = Services.BeginLifetimeScope();
            var tblKeyValue = scope.Resolve<TableKeyValue>();

            for (int i = 0; i < 10000; i++)
            {
                Rows.Add(Guid.NewGuid().ToByteArray());
                await tblKeyValue.InsertAsync(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
            }

            Thread tw1 = new Thread(() => writeDB1());
            Thread tw2 = new Thread(() => writeDB2());
            Thread tr = new Thread(() => readDB());

            tw1.Start();
            tw2.Start();
            tr.Start();

            tw1.Join();
            tw2.Join();
            tr.Join();
        }

        /// <summary>
        /// This test passes because the Database class locks on it's _transactionLock() object
        /// </summary>
        [Test, Ignore("Deprecated since scoped connections")]
        public Task ReaderLockingTest()
        {
            return Task.CompletedTask;
            // List<byte[]> Rows = new List<byte[]>();
            //
            // void writeDB1( DatabaseConnection myc)
            // {
            //     for (int i = 0; i < 10000; i++)
            //     {
            //         tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.Empty.ToByteArray(), data = Guid.NewGuid().ToByteArray() }).Wait();
            //     }
            // }
            //
            // void readDB( DatabaseConnection myc)
            // {
            //     for (int i = 0; i < 3; i++)
            //     {
            //         var r = tblKeyTwoValue.GetByKeyTwoAsync(Guid.Empty.ToByteArray()).Result;
            //         if (r.Count != 10000)
            //             Assert.Fail();
            //     }
            // }
            //
            // using var db = new IdentityDatabase(Guid.NewGuid(), ""); // 1ms commit frequency
            //
            // using (var myc = CreateDisposableConnection())
            // {
            //     await CreateDatabaseAsync();
            //
            //     for (int i = 0; i < 10000; i++)
            //     {
            //         Rows.Add(Guid.NewGuid().ToByteArray());
            //         await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.Empty.ToByteArray(), data = Guid.NewGuid().ToByteArray() });
            //     }
            //
            //     Thread tr = new Thread(() => readDB(db, myc));
            //     Thread tw1 = new Thread(() => writeDB1(db, myc));
            //
            //     tr.Start();
            //     tw1.Start();
            //
            //     tw1.Join();
            //     tr.Join();
            // }
        }


        /// <summary>
        /// This test will fail because the two connections cannot both access the database (by design)
        /// </summary>
        [Test, Ignore("Deprecated since scoped connections")]
        public Task TwoInstanceLockingTest()
        {
            return Task.CompletedTask;

            // using var db1 = new IdentityDatabase(Guid.NewGuid(), "DataSource=mansi.db");
            //
            // using (var myc = db1.CreateDisposableConnection())
            // {
            //     await db1.CreateDatabaseAsync();
            //     try
            //     {
            //         using var db2 = new IdentityDatabase(Guid.NewGuid(), "DataSource=mansi.db");
            //         Assert.Fail("It's supposed to do a database lock");
            //     }
            //     catch (Exception ex)
            //     {
            //         Assert.Pass(ex.Message);
            //     }
            // }
        }
    }
}
