using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Dto;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.CorrelationId.Serilog;
using Odin.PushNotification;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

var builder = WebApplication.CreateBuilder(args);

//
// Logging
//

const string logOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Message:lj}{NewLine}{Exception}";
var logOutputTheme = SystemConsoleTheme.Literate;

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
    .WriteTo.Console(outputTemplate: logOutputTemplate, theme: logOutputTheme));

//
// Swagger
//

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Firebase Messaging

var firebaseCredentialsFile = builder.Configuration["Firebase:CredentialsFileName"] ?? "";
builder.Services.AddSingleton<IPushNotification>(new PushNotification(firebaseCredentialsFile));

builder.Services.AddFluentValidationAutoValidation()
    .AddValidatorsFromAssemblyContaining<PushNotificationRequestValidator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/message", async (
        ILogger<PushNotification> logger,
        IPushNotification pushNotification,
        IValidator<PushNotificationRequest> validator,
        [FromBody] PushNotificationRequest request) =>
    {
        var validationResult = validator.Validate(request);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(validationResult.Errors);
        }

        try
        {
            var response = await pushNotification.Post(request);
            logger.LogInformation("Successfully sent message: {response}", response);
            return Results.Ok();

        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending message: {error}", e.Message);
            return Results.Problem("An internal error occurred.", statusCode: 500);
        }
    })
    .WithName("Post")
    .WithOpenApi();

app.Run();

//

public class PushNotificationRequestValidator : AbstractValidator<PushNotificationRequest>
{
    public PushNotificationRequestValidator()
    {
        RuleFor(request => request.DeviceToken).NotEmpty();
        RuleFor(request => request.Title).NotEmpty();
        RuleFor(request => request.Body).NotEmpty();

        // SEB:TODO the sender signs the message using his private key from his SSL certificate.
        // The recipient then uses OriginDomain to look up the public key from the sender's SSL certificate
        // and verifies the signature.
    }
}

//