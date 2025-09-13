using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Notary;
using Odin.Core.Storage.Database.Notary.Table;
using Odin.Core.Time;

namespace Odin.NotariusTests;

public class Tests
{
    private IContainer _container = null!;
    private NotaryDatabase _db = null!;

    [SetUp]
    public void Setup()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole());
        serviceCollection.AddSingleton<INodeLock, NodeLock>();

        var builder = new ContainerBuilder();
        builder.Populate(serviceCollection);


        builder.AddDatabaseServices();
        builder.AddSqliteNotaryDatabaseServices(":memory:");

        _container = builder.Build();
        _db = _container.Resolve<NotaryDatabase>();
    }

    [TearDown]
    public void TearDown()
    {
        _container.Dispose();
    }

    [Test]
    public async Task Test1()
    {
        await _db.MigrateDatabaseAsync();

        var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

        var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
        var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
        var r = new NotaryChainRecord()
        {
            previousHash = hash,
            identity = "frodo.baggins.me",
            signedPreviousHash = key,
            algorithm = "ublah",
            publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
            recordHash = hash,
            timestamp = UnixTimeUtc.Now(),
            notarySignature = Guid.Empty.ToByteArray()
        };
        await _db.NotaryChain.InsertAsync(r);

        Assert.Pass();
    }
}
