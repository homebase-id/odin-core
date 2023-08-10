using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Exceptions.Client;
using Odin.Core.Exceptions.Server;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Hosting.Middleware;

namespace Odin.Hosting.Tests.Middleware;

public class ExceptionHandlingMiddlewareTest
{
    private const string MockCorrelationId = "helloworld";
    private static TestServer CreateTestServer(string environment, RequestDelegate requestDelegate)
    {
        var mockHostEnvironment = new Mock<IHostEnvironment>();
        mockHostEnvironment.Setup(env => env.EnvironmentName).Returns(environment);

        var mockCorrelationIdGenerator = new Mock<ICorrelationIdGenerator>();
        mockCorrelationIdGenerator.Setup(generator => generator.Generate()).Returns(MockCorrelationId);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHostEnvironment>(mockHostEnvironment.Object);
                services.AddSingleton<ICorrelationIdGenerator>(mockCorrelationIdGenerator.Object);
                services.AddSingleton<ICorrelationContext, CorrelationContext>();
            })
            .Configure(app =>
            {
                app.UseMiddleware<ExceptionHandlingMiddleware>();
                app.Run(requestDelegate);
            }));
    }

    [Test]
    public async Task NoErrorHere()
    {
        // Arrange
        var server = CreateTestServer(Environments.Production, async ctx =>
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
    }

    [Test]
    public async Task NotFoundExceptionInProduction()
    {
        // Arrange
        var server = CreateTestServer(Environments.Production, async ctx =>
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
    }

    [Test]
    public async Task NotFoundExceptionInDevelopment()
    {
        // Arrange
        var server = CreateTestServer(Environments.Development, async ctx =>
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
    }

    [Test]
    public async Task InternalServerErrorExceptionInProduction()
    {
        // Arrange
        var server = CreateTestServer(Environments.Production, async ctx =>
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
    }

    [Test]
    public async Task InternalServerErrorExceptionInDevelopment()
    {
        // Arrange
        var server = CreateTestServer(Environments.Development, async ctx =>
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
    }

    [Test]
    public async Task GenericExceptionInProduction()
    {
        // Arrange
        var server = CreateTestServer(Environments.Production, async ctx =>
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
    }

    [Test]
    public async Task GenericExceptionInDevelopment()
    {
        // Arrange
        var server = CreateTestServer(Environments.Development, async ctx =>
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
    }
}