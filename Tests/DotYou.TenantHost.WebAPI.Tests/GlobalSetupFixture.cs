using DotYou.TenantHost;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// This runs once for all tests in the assembly
/// </summary>
[SetUpFixture]
public class GlobalSetupFixture
{
    private static IHost webserver;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        string testDataPath = @"\temp\dotyoudata";

        if(Directory.Exists(testDataPath))
        {
            Console.WriteLine($"Removing data in [{testDataPath}]");
            Directory.Delete(testDataPath, true);
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("DATA_ROOT_PATH", testDataPath);
        var args = new string[0];
        webserver = Program.CreateHostBuilder(args).Build();
        webserver.Start();
    }

    [OneTimeTearDown]
    public void RunAfterAnyTests()
    {
        System.Threading.Thread.Sleep(2000);
        webserver.StopAsync();
    }
}
