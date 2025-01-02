using System;
using System.Runtime.InteropServices;
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

    [Test]
    public void ItShouldCreateHomeEnvVariableOnWindowsIfItDoesntExist()
    {
        // This seemingly redundant call is necessary to initialize the static constructor on Env
        Env.IsDevelopment(); 
        
        var home = Environment.GetEnvironmentVariable("HOME");
        Assert.IsNotNull(home);
        Assert.IsNotEmpty(home);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("Windows %HOME%: " + home);    
        }
    }
    
    [Test]
    public void ItShouldExpandEnvVariablesCrossPlatform()
    {
        {
            Environment.SetEnvironmentVariable("FOO", "foo");
            Environment.SetEnvironmentVariable("BAR", "bar");
            var expanded = Env.ExpandEnvironmentVariablesCrossPlatform("$FOO/${BAR}/baz");
            Assert.That(expanded, Is.EqualTo("foo/bar/baz"));
        }
      
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var expanded = Env.ExpandEnvironmentVariablesCrossPlatform("%HOME%/foo/bar");
            Assert.That(expanded, Is.EqualTo(Environment.GetEnvironmentVariable("HOME") + "/foo/bar"));
        }
        else
        {
            var expanded = Env.ExpandEnvironmentVariablesCrossPlatform("$HOME/foo/bar");
            Assert.That(expanded, Is.EqualTo(Environment.GetEnvironmentVariable("HOME") + "/foo/bar"));
        }
        
        var key = $"ENV_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(key, key);
        Assert.That(Env.ExpandEnvironmentVariablesCrossPlatform(key), Is.EqualTo(key));
    }
}