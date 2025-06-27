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
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Tests.Http;

#nullable enable

public class OdinHttpClientFactoryTests
{
    private ILogEventMemoryStore _logEventMemoryStore = null!;
    private ILogger<OdinHttpClientFactory> _logger = null!;

    [SetUp]
    public void Setup()
    {
        _logEventMemoryStore = new LogEventMemoryStore();
        _logger = TestLogFactory.CreateConsoleLogger<OdinHttpClientFactory>(_logEventMemoryStore);
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public async Task TestOdinHttpClientFactoryCreation()
    {
        // Arrange
        using (var factory = new OdinHttpClientFactory(_logger))
        {
            await Task.Delay(100); // Simulate some async work
        }
        ;
    }

    //

    [Test]
    public async Task CreateClient_WithSameNameAndConfig_ReusesExistingHandler()
    {
        // Arrange
        using var factory = new OdinHttpClientFactory(_logger);

        // First client
        var client1 = factory.CreateClient("test1", config =>
        {
            config.DefaultHeaders.Add("Test-Header1", "Value");
        });

        // Second client, same name and config
        var client2 = factory.CreateClient("test1", config =>
        {
            config.DefaultHeaders.Add("Test-Header1", "Value");
        });

        // Third client, new name, same config
        var client3 = factory.CreateClient("test2", config =>
        {
            config.DefaultHeaders.Add("Test-Header1", "Value");
        });

        // Fourth client, new name, new config
        var client4 = factory.CreateClient("test2", config =>
        {
            config.DefaultHeaders.Add("Test-Header2", "Value");
        });

        // Act
        var response1 = await client1.GetAsync("https://www.google.com");
        var response2 = await client2.GetAsync("https://www.google.com");
        var response3 = await client3.GetAsync("https://www.google.com");
        var response4 = await client3.GetAsync("https://www.google.com");

        // Assert responses are correct
        Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response3.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response4.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var messages = logEvents[LogEventLevel.Debug].Select(x => x.RenderMessage().Trim('"')).ToList();
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
        using var factory = new OdinHttpClientFactory(_logger);
        var handlerLogger = TestLogFactory.CreateConsoleLogger<LoggingHandler>(_logEventMemoryStore);

        var handler1 = new LoggingHandler(handlerLogger, "Handler1: Request Sent", true);
        var handler2 = new LoggingHandler(handlerLogger, "Handler2: Request Sent", true);
        var handler3 = new LoggingHandler(handlerLogger, "Handler3: Response Received", false);

        // Configure the client with multiple handlers
        var client = factory.CreateClient(clientName, config =>
        {
            config.BaseAddress = new Uri("https://www.google.com");
            config.CustomHandlerFactories.Add(inner => handler1.SetInnerHandler(inner));
            config.CustomHandlerFactories.Add(inner => handler2.SetInnerHandler(inner));
            config.CustomHandlerFactories.Add(inner => handler3.SetInnerHandler(inner));
        });

        // Act
        var response = await client.GetAsync("/");
        _logger.LogInformation("{StatusCode}", response.StatusCode);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logEvents = _logEventMemoryStore.GetLogEvents();
        var infoEvents = logEvents[LogEventLevel.Information].Select(x => x.RenderMessage().Trim('"')).ToList();
        Assert.That(infoEvents, Does.Contain("Handler1: Request Sent"));
        Assert.That(infoEvents.IndexOf("Handler1: Request Sent"), Is.LessThan(infoEvents.IndexOf("Handler2: Request Sent")));
    }
}

// Custom DelegatingHandler for logging
public class LoggingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHandler> _logger;
    private readonly string _logMessage;
    private readonly bool _beforeInner;

    public LoggingHandler(ILogger<LoggingHandler> logger, string logMessage, bool beforeInner)
    {
        _logMessage = logMessage;
        _logger = logger;
        _beforeInner = beforeInner;
    }

    public LoggingHandler SetInnerHandler(HttpMessageHandler innerHandler)
    {
        InnerHandler = innerHandler;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_beforeInner)
        {
            _logger.LogInformation("{message}", _logMessage);
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (!_beforeInner)
        {
            _logger.LogInformation("{message} {status}", _logMessage, response.StatusCode);
        }

        return response;
    }
}


