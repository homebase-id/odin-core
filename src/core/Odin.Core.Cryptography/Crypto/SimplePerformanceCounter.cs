using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nito.AsyncEx;
using Odin.Core.Cryptography.Data;
using Org.BouncyCastle.Security;

namespace Odin.Core.Cryptography.Crypto
{
    public static class SimplePerformanceCounter
    {
        // Only increase with Interlocked.Increment(ref counter);

        public static int noRsaKeysCreated;
        public static int noRsaKeysExpired;
        public static int noRsaKeysCreatedTest;
        public static int noRsaEncryptions;
        public static int noRsaDecryptions;

        public static int noDBOpened;
        public static int noDBClosed;

        public static int noDBExecuteNonQueryAsync;
        public static int noDBExecuteReaderAsync;
        public static int noDBExecuteScalar;
        
        static SimplePerformanceCounter()
        {
            Reset();
        }


        public static void Reset()
        {
            Interlocked.Exchange(ref noRsaKeysCreated, 0);
            Interlocked.Exchange(ref noRsaKeysExpired, 0);
            Interlocked.Exchange(ref noRsaKeysCreatedTest, 0);
            Interlocked.Exchange(ref noRsaEncryptions, 0);
            Interlocked.Exchange(ref noRsaDecryptions, 0);

            Interlocked.Exchange(ref noDBOpened, 0);
            Interlocked.Exchange(ref noDBClosed, 0);

            Interlocked.Exchange(ref noDBExecuteNonQueryAsync, 0);
            Interlocked.Exchange(ref noDBExecuteReaderAsync, 0);
            Interlocked.Exchange(ref noDBExecuteScalar, 0);
        }

        public static string DumpRSA()
        {
            string s;

            s =  $"RSA Keys Created        \t{SimplePerformanceCounter.noRsaKeysCreated}{Environment.NewLine}";
            s += $"RSA Keys Expired        \t{SimplePerformanceCounter.noRsaKeysExpired}{Environment.NewLine}";
            s += $"RSA Keys Created (Test) \t{SimplePerformanceCounter.noRsaKeysCreatedTest}{Environment.NewLine}";
            s += $"RSA Encryptions         \t{SimplePerformanceCounter.noRsaEncryptions}{Environment.NewLine}";
            s += $"RSA Decryptions         \t{SimplePerformanceCounter.noRsaDecryptions}{Environment.NewLine}";

            return s;
        }

        public static string DumpDB()
        {
            string s;

            s =  $"DB Opened               \t{SimplePerformanceCounter.noDBOpened}{Environment.NewLine}";
            s += $"DB Closed               \t{SimplePerformanceCounter.noDBClosed}{Environment.NewLine}";
            s += $"DB ExecuteNonQueryAsync \t{SimplePerformanceCounter.noDBExecuteNonQueryAsync}{Environment.NewLine}";
            s += $"DB ExecuteReaderAsync   \t{SimplePerformanceCounter.noDBExecuteReaderAsync}{Environment.NewLine}";
            s += $"DB ExecuteScalar        \t{SimplePerformanceCounter.noDBExecuteScalar}{Environment.NewLine}";

            return s;
        }


        public static string Dump()
        {
            return DumpDB() + DumpRSA();
        }
    }


    public static class EccKeyManagement
    {
        public static int noKeysCreated = 0;
        public static int noKeysExpired = 0;
        public static int noKeysCreatedTest = 0;
        public static int noEncryptions = 0;
        public static int noDecryptions = 0;

        public static int noDBOpened = 0;
        public static int noDBClosed = 0;
    }
}
