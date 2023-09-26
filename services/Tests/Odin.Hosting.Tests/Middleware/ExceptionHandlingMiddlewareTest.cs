using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Hosting.ApiExceptions.Server;
using Odin.Hosting.Middleware;

namespace Odin.Hosting.Tests.Middleware;

// https://adamstorr.azurewebsites.net/blog/mocking-ilogger-with-moq

public class ExceptionHandlingMiddlewareTest
{
    private const string MockCorrelationId = "helloworld";
    private static TestServer CreateTestServer(
        string environment,
        ILogger<ExceptionHandlingMiddleware> logger,
        RequestDelegate requestDelegate)
    {
        var mockHostEnvironment = new Mock<IHostEnvironment>();
        mockHostEnvironment.Setup(env => env.EnvironmentName).Returns(environment);

        var mockCorrelationId = new Mock<ICorrelationContext>();
        mockCorrelationId.Setup(correlationId => correlationId.Id).Returns(MockCorrelationId);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                // Add services here.
                // We have to hardcode the logger instead, otherwise the mock isn't picked up.
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    var middleware = new ExceptionHandlingMiddleware(
                        next,
                        logger,
                        mockCorrelationId.Object,
                        mockHostEnvironment.Object);

                    await middleware.Invoke(context);
                });

                app.Run(requestDelegate);
            }));
    }

    [Test]
    public async Task NoErrorHere()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Production, loggerMock.Object, async ctx =>
        {
            await ctx.Response.WriteAsync("No error here!");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content, Is.EqualTo("No error here!"));

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Never);
    }

    [Test]
    public async Task NotFoundExceptionInProduction()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Production, loggerMock.Object, async ctx =>
        {
            await Task.CompletedTask;
            throw new NotFoundException(message: "my-error-message");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo((int)HttpStatusCode.NotFound));
        Assert.That(problems.Title, Is.EqualTo("my-error-message"));
        Assert.That(Enum.Parse<OdinClientErrorCode>(problems.Extensions["errorCode"].ToString()!),
            Is.EqualTo(OdinClientErrorCode.NoErrorCode));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.False);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Never);
    }

    [Test]
    public async Task NotFoundExceptionInDevelopment()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Development, loggerMock.Object, async ctx =>
        {
            await Task.CompletedTask;
            throw new NotFoundException(
                message: "my-error-message",
                odinClientErrorCode: OdinClientErrorCode.FileNotFound);
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo((int)HttpStatusCode.NotFound));
        Assert.That(problems.Title, Is.EqualTo("my-error-message"));
        Assert.That(Enum.Parse<OdinClientErrorCode>(problems.Extensions["errorCode"].ToString()!),
            Is.EqualTo(OdinClientErrorCode.FileNotFound));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.True);
        Assert.That(problems.Extensions["stackTrace"].ToString(), Is.Not.Empty.And.Not.Null);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Never);
    }

    [Test]
    public async Task InternalServerErrorExceptionInProduction()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Production, loggerMock.Object, async ctx =>
        {
            await Task.CompletedTask;
            throw new InternalServerErrorException(message: "this-will-not-reach-client-in-production");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo((int)HttpStatusCode.InternalServerError));
        Assert.That(problems.Title, Is.EqualTo("Internal Server Error"));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.False);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

    [Test]
    public async Task InternalServerErrorExceptionInDevelopment()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Development, loggerMock.Object, async ctx =>
        {
            await Task.CompletedTask;
            throw new InternalServerErrorException(message: "this-will-reach-client-in-development");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo((int)HttpStatusCode.InternalServerError));
        Assert.That(problems.Title, Is.EqualTo("this-will-reach-client-in-development"));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.True);
        Assert.That(problems.Extensions["stackTrace"].ToString(), Is.Not.Empty.And.Not.Null);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

    [Test]
    public async Task GenericExceptionInProduction()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Production, loggerMock.Object, async ctx =>
        {
            // Division by zero:
            await ctx.Response.WriteAsync($"{1 / "".Length}");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo((int)HttpStatusCode.InternalServerError));
        Assert.That(problems.Title, Is.EqualTo("Internal Server Error"));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.False);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

    [Test]
    public async Task GenericExceptionInDevelopment()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Development, loggerMock.Object, async ctx =>
        {
            // Division by zero:
            await ctx.Response.WriteAsync($"{1 / "".Length}");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo((int)HttpStatusCode.InternalServerError));
        Assert.That(problems.Title, Is.EqualTo("Attempted to divide by zero."));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.True);
        Assert.That(problems.Extensions["stackTrace"].ToString(), Is.Not.Empty.And.Not.Null);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

    [Test]
    public async Task CancellationExceptionInProduction()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Production, loggerMock.Object, async ctx =>
        {
            await Task.CompletedTask;
            throw new OperationCanceledException("run away!");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That((int)response.StatusCode, Is.EqualTo(499));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo(499));
        Assert.That(problems.Title, Is.EqualTo("Operation was cancelled"));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.False);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

    [Test]
    public async Task CancellationExceptionInDevelopment()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var server = CreateTestServer(Environments.Development, loggerMock.Object, async ctx =>
        {
            await Task.CompletedTask;
            throw new OperationCanceledException("run away!");
        });
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That((int)response.StatusCode, Is.EqualTo(499));

        var problems = OdinSystemSerializer.Deserialize<ProblemDetails>(content);
        Assert.That(problems.Status, Is.EqualTo(499));
        Assert.That(problems.Title, Is.EqualTo("run away!"));
        Assert.That(problems.Extensions["correlationId"].ToString(), Is.EqualTo(MockCorrelationId));
        Assert.That(problems.Extensions.ContainsKey("stackTrace"), Is.True);
        Assert.That(problems.Extensions["stackTrace"].ToString(), Is.Not.Empty.And.Not.Null);

        loggerMock.Verify(x =>
            x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

}