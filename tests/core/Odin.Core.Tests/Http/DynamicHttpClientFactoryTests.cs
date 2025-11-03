using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.X509;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Tests.Http;

#nullable enable

public class DynamicHttpClientFactoryTests
{
    private ILogEventMemoryStore _logEventMemoryStore = null!;
    private ILogger<DynamicHttpClientFactory> _logger = null!;

    [SetUp]
    public void Setup()
    {
        _logEventMemoryStore = new LogEventMemoryStore();
        _logger = TestLogFactory.CreateConsoleLogger<DynamicHttpClientFactory>(_logEventMemoryStore, LogEventLevel.Verbose);
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public async Task CreateClient_WorksWith_DefaultConfig()
    {
        using var factory = new DynamicHttpClientFactory(_logger);
        var client1 = factory.CreateClient("test1");
        var response1 = await client1.GetAsync("https://www.google.com");
        Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    //

    [Test]
    public async Task CreateClient_WithSameNameAndConfig_ReusesExistingHandler()
    {
        // Arrange
        using var factory = new DynamicHttpClientFactory(_logger);

        // First client
        var client1 = factory.CreateClient("test1", config =>
        {
            // config.HandlerLifetime 2mins is default
        });

        // Second client, same name and config
        var client2 = factory.CreateClient("test1", config =>
        {
            config.HandlerLifetime = TimeSpan.FromMinutes(2);
        });

        // Third client, new name, same config
        var client3 = factory.CreateClient("test2", config =>
        {
            config.HandlerLifetime = TimeSpan.FromMinutes(2);
        });

        // Fourth client, same name, new config
        var client4 = factory.CreateClient("test2", config =>
        {
            config.HandlerLifetime = TimeSpan.FromMinutes(10);
        });

        // Act
        var response1 = await client1.GetAsync("https://www.google.com");
        var response2 = await client2.GetAsync("https://www.google.com");
        var response3 = await client3.GetAsync("https://www.google.com");
        var response4 = await client4.GetAsync("https://www.google.com");

        // Assert responses are correct
        Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response3.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response4.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var messages = logEvents[LogEventLevel.Verbose].Select(x => x.RenderMessage().Trim('"')).ToList();
        Assert.That(messages.Count(x => x.StartsWith("Created new handler for key \"test1-")), Is.EqualTo(1));
        Assert.That(messages.Count(x => x.StartsWith("Created HttpClient for \"test1\" with handler key \"test1-")), Is.EqualTo(2));
        Assert.That(messages.Count(x => x.StartsWith("Created new handler for key \"test2-")), Is.EqualTo(2));
        Assert.That(messages.Count(x => x.StartsWith("Created HttpClient for \"test2\" with handler key \"test2-")), Is.EqualTo(2));
    }

    //

    [Test]
    public async Task CreateClient_WithMultipleHandlers_LogsMessagesFromBothHandlers()
    {
        // Arrange
        var clientName = "TestClient";
        using var factory = new DynamicHttpClientFactory(_logger);
        var handlerLogger = TestLogFactory.CreateConsoleLogger<LoggingHandler>(_logEventMemoryStore);

        // Configure the client with multiple handlers
        var client = factory.CreateClient(clientName, config =>
        {
            config.MessageHandlerChain.Add(inner => new LoggingHandler(handlerLogger, "Handler1: Request Sent", true, inner));
            config.MessageHandlerChain.Add(inner => new LoggingHandler(handlerLogger, "Handler2: Request Sent", true, inner));
            config.MessageHandlerChain.Add(inner => new LoggingHandler(handlerLogger, "Handler3: Response Received", false, inner));
        });

        // Act
        client.BaseAddress = new Uri("https://www.google.com");
        var response = await client.GetAsync("/");
        _logger.LogInformation("{StatusCode}", response.StatusCode);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var infoEvents = logEvents[LogEventLevel.Information].Select(x => x.RenderMessage().Trim('"')).ToList();
        Assert.That(infoEvents, Does.Contain("Handler1: Request Sent"));
        Assert.That(infoEvents.IndexOf("Handler1: Request Sent"), Is.LessThan(infoEvents.IndexOf("Handler2: Request Sent")));
    }

    //

    [Test]
    public async Task FactoryShould_DisposeHandler_AfterExpiryAndGracePeriod()
    {
        // Arrange
        using var factory = new DynamicHttpClientFactory(
            logger: _logger,
            defaultHandlerLifetime: TimeSpan.FromMilliseconds(100),
            cleanupInterval: TimeSpan.FromMilliseconds(20),
            disposeGracePeriod: TimeSpan.FromMilliseconds(1000));

        var client = factory.CreateClient("www.google.com");

        Assert.That(factory.CountActiveHandlers(), Is.EqualTo(1));
        Assert.That(factory.CountExpiredHandlers(), Is.EqualTo(0));

        var response = await client.GetAsync("https://www.google.com");

        await Task.Delay(500);

        Assert.That(factory.CountActiveHandlers(), Is.EqualTo(0));
        Assert.That(factory.CountExpiredHandlers(), Is.EqualTo(1));

        await Task.Delay(1500);

        Assert.That(factory.CountActiveHandlers(), Is.EqualTo(0));
        Assert.That(factory.CountExpiredHandlers(), Is.EqualTo(0));
    }

    //

    [Test]
    public async Task FactoryShould_IncrementAndDecrement_ActiveRequests()
    {
        // Arrange
        using var factory = new DynamicHttpClientFactory(
            logger: _logger,
            defaultHandlerLifetime: TimeSpan.FromMinutes(100),
            cleanupInterval: TimeSpan.FromMilliseconds(20),
            disposeGracePeriod: TimeSpan.FromMilliseconds(100));

        var client = factory.CreateClient("www.google.com");
        await client.GetAsync("https://www.google.com");

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var events = logEvents[LogEventLevel.Verbose].Select(x => x.RenderMessage().Trim('"')).ToList();
        Assert.That(events, Has.Some.StartsWith("BeforeSend ActiveRequests=1"));
        Assert.That(events, Has.Some.StartsWith("AfterSend ActiveRequests=0"));
    }

    //

    [Test]
    public async Task CreateClient_WithSameCertificate_ReusesExistingHandler()
    {
        // Arrange
        using var factory = new DynamicHttpClientFactory(_logger);
        var selfSigned = X509Extensions.CreateSelfSignedEcDsaCertificate("www.example.com");

        // First client
        var client1 = factory.CreateClient("test1", config =>
        {
            config.ClientCertificate = selfSigned;
        });

        // Second client
        var client2 = factory.CreateClient("test1", config =>
        {
            config.ClientCertificate = selfSigned;
        });

        // Act
        var response1 = await client1.GetAsync("https://www.google.com");
        var response2 = await client2.GetAsync("https://www.google.com");

        // Assert responses are correct
        Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var messages = logEvents[LogEventLevel.Verbose].Select(x => x.RenderMessage().Trim('"')).ToList();
        Assert.That(messages.Count(x => x.StartsWith("Created new handler for key \"test1-")), Is.EqualTo(1));
    }

    //

    [Test]
    public async Task CreateClient_WithDifferentCertificate_DontReusExistingHandler()
    {
        // Arrange
        using var factory = new DynamicHttpClientFactory(_logger);
        var selfSigned1 = X509Extensions.CreateSelfSignedEcDsaCertificate("www.example.com");
        var selfSigned2 = X509Extensions.CreateSelfSignedEcDsaCertificate("www.example.com");

        // First client
        var client1 = factory.CreateClient("test1", config =>
        {
            config.ClientCertificate = selfSigned1;
        });

        // Second client
        var client2 = factory.CreateClient("test1", config =>
        {
            config.ClientCertificate = selfSigned2;
        });

        // Act
        var response1 = await client1.GetAsync("https://www.google.com");
        var response2 = await client2.GetAsync("https://www.google.com");

        // Assert responses are correct
        Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var messages = logEvents[LogEventLevel.Verbose].Select(x => x.RenderMessage().Trim('"')).ToList();
        Assert.That(messages.Count(x => x.StartsWith("Created new handler for key \"test1-")), Is.EqualTo(2));
    }
}


// Custom DelegatingHandler for logging
public class LoggingHandler(
    ILogger<LoggingHandler> logger,
    string logMessage,
    bool beforeInner,
    HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (beforeInner)
        {
            logger.LogInformation("{message}", logMessage);
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (!beforeInner)
        {
            logger.LogInformation("{message} {status}", logMessage, response.StatusCode);
        }

        return response;
    }
}


