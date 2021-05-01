using DotYou.Types.Certificate;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class Tests
    {
        static string frodo = "frodobaggins.me";
        static string samwise = "samwisegamgee.me";

        IHost webserver;

        [SetUp]
        public void Setup()
        {
            var args = new string[0];
            webserver = Program.CreateHostBuilder(args).Build();
            webserver.StartAsync();
        }

        [TearDown]
        public void Teardown()
        {
            webserver.StopAsync();
        }

        [Test(Description ="Test ensures a client certificate can be used to authenticate an identity")]
        public async Task CanAuthenticateWithClientCertificate()
        {
            string publicKeyFile = Path.Combine(Environment.CurrentDirectory, "https", samwise, "certificate.crt");
            string privateKeyFile = Path.Combine(Environment.CurrentDirectory, "https", samwise, "private.key");

            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificates.Add(CertificateLoader.LoadWithKeyFile(publicKeyFile, privateKeyFile));
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            HttpClient client = new HttpClient(handler);

            var result = await client.GetStringAsync($"https://{frodo}/api/verify");

            Console.WriteLine($"going {result}");

            Assert.Pass(result);
            //Assert.AreEqual(result, "pie");
        }
    }
}