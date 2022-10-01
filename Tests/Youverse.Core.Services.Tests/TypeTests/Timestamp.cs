using System;
using NUnit.Framework;

namespace Youverse.Core.Services.Tests.TypeTests;

public struct Timestamp
{
    public long EpochTimeUtc { get; set; }
    
    public TimeZoneInfo Timezone { get; set; }
}

public class TimestampTests
{
    // [Test]
    // public void Test1()
    // {
    //    
    // }
}