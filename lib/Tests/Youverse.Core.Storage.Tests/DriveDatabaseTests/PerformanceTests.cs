using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace IndexerTests.KeyValue
{
    public class PerformanceTests
    {
        private const int _performanceIterations = 5000; // Set to 5,000 when testing

        [Test]
        public void PerformanceTest01()
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            var _testDatabase = new DriveIndexDatabase($"URI=file:.\\performance-01", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var tmpacllist = new List<Guid>();
            for (int j = 0; j < 1; j++)
            {
                tmpacllist.Add(Guid.NewGuid());
            }

            var tmptaglist = new List<Guid>();
            for (int j = 0; j < 1; j++)
            {
                tmptaglist.Add(Guid.NewGuid());
            }

            stopWatch.Start();
            for (int i=1; i < _performanceIterations; i++)
            {
                _testDatabase.AddEntry(Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToByteArray(), Guid.NewGuid(), Guid.NewGuid(), 0, 55, tmpacllist, tmptaglist);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            Utils.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations*1000) / ms}");
        }


        [Test]
        public void PerformanceTest02() // Test batch of 100
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            var _testDatabase = new DriveIndexDatabase($"URI=file:.\\performance-02", DatabaseIndexKind.TimeSeries);
            _testDatabase.CreateDatabase();

            var tmpacllist = new List<Guid>();
            for (int j = 0; j < 1; j++)
            {
                tmpacllist.Add(Guid.NewGuid());
            }

            var tmptaglist = new List<Guid>();
            for (int j = 0; j < 1; j++)
            {
                tmptaglist.Add(Guid.NewGuid());
            }

            stopWatch.Start();
            _testDatabase.BeginTransaction();
            for (int i = 1; i < _performanceIterations; i++)
            {
                _testDatabase.AddEntry(Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToByteArray(), Guid.NewGuid(), Guid.NewGuid(), 0, 55, tmpacllist, tmptaglist);
                if (i % 100 ==0)
                {
                    _testDatabase.Commit();
                    _testDatabase.BeginTransaction();
                }
            }
            _testDatabase.Commit();
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            Utils.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000) / ms}");
        }
    }
}
