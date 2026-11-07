using System;
using System.Net.Http;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Authorization.Capi;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Tests.Base;

public class OdinHttpClientFactoryTests
{
    private static readonly TimeSpan FileTransferTimeout = TimeSpan.FromMinutes(17);
    private static readonly OdinId Peer = new("sam.dotyou.cloud");

    private static (OdinHttpClientFactory factory, HttpClient httpClient) Create()
    {
        var httpClient = new HttpClient();
        var httpClientFactory = Substitute.For<IDynamicHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>(), Arg.Any<Action<ClientHandlerConfig>?>()).Returns(httpClient);

        var capiSession = Substitute.For<ICapiCallbackSession>();
        capiSession.EstablishSessionAsync(Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns("a-session");

        var correlationContext = Substitute.For<ICorrelationContext>();
        correlationContext.Id.Returns("a-correlation-id");

        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                PeerFileTransferTimeout = FileTransferTimeout
            }
        };

        return (new OdinHttpClientFactory(httpClientFactory, correlationContext, config, capiSession,
            new OdinIdentity(Guid.NewGuid(), "frodo.dotyou.cloud")), httpClient);
    }

    private static ClientAuthenticationToken Token() => new()
    {
        Id = Guid.NewGuid(),
        AccessTokenHalfKey = Guid.NewGuid().ToByteArray().ToSensitiveByteArray()
    };

    [Test]
    public async Task AFileTransferClientHasTheConfiguredTimeoutInEveryBuild()
    {
        var (factory, httpClient) = Create();

        await factory.CreateFileTransferClientUsingAccessTokenAsync<IPeerTransferHttpClient>(Peer, Token());

        Assert.That(httpClient.Timeout, Is.EqualTo(FileTransferTimeout));
    }

    [Test]
    public async Task AnyOtherPeerClientDoesNotGetIt()
    {
        var (factory, httpClient) = Create();

        await factory.CreateClientUsingAccessTokenAsync<IPeerTransferHttpClient>(Peer, Token());

        Assert.That(httpClient.Timeout, Is.Not.EqualTo(FileTransferTimeout));
    }
}
