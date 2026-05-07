using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;
using Serilog.Events;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests;

#nullable enable

public abstract class IocTestBase
{
    private TestServices? _testServices;

    protected string TempFolder = "";
    protected ILifetimeScope Services = null!;
    protected Guid IdentityId;

    [SetUp]
    public virtual void Setup()
    {
        IdentityId = Guid.NewGuid();
        TempFolder = TempDirectory.Create();
        _testServices = new TestServices();
    }

    [TearDown]
    public virtual void TearDown()
    {
        var logEventMemoryStore = Services?.Resolve<ILogEventMemoryStore>();
        if (logEventMemoryStore != null)
        {
            LogEvents.DumpErrorEvents(logEventMemoryStore.GetLogEvents());
            LogEvents.AssertEvents(logEventMemoryStore.GetLogEvents());
        }

        _testServices?.Dispose();
        _testServices = null;
        Services = null!;

        Directory.Delete(TempFolder, true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    //

    protected virtual async Task RegisterServicesAsync(
        DatabaseType databaseType,
        bool createDatabases = true,
        bool redisEnabled = false,
        LogEventLevel logEventLevel = LogEventLevel.Debug)
    {
        Services = await _testServices!.RegisterServicesAsync(
            databaseType,
            TempFolder,
            IdentityId,
            createDatabases,
            redisEnabled,
            logEventLevel: logEventLevel);
    }

    // Wipe the L1 (in-memory) cache that FusionCache is using. TestServices owns and held a
    // reference to the MemoryCache it handed FusionCache via WithMemoryCache, so this evicts
    // every entry FusionCache currently has in L1 — the next read falls through to L2 (Redis,
    // when redisEnabled=true) and exercises the JSON serialize/deserialize path.
    //
    // Test-controlled, not subscribed to FusionCache events. Call this between writes and
    // reads in any test that wants to force an L2 round-trip.
    protected void WipeL1()
    {
        _testServices!.L1MemoryCache!.Compact(1.0);
    }
}