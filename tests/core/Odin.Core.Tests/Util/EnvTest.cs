using System;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class EnvTest
{
    [Test]
    public void TestIsDevelopment()
    {
        var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Assert.IsTrue(Env.IsDevelopment());
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "development");
        Assert.IsTrue(Env.IsDevelopment());
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "DEVELOPMENT");
        Assert.IsTrue(Env.IsDevelopment());
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Assert.IsFalse(Env.IsDevelopment());
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "production");
        Assert.IsFalse(Env.IsDevelopment());

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
    }
}