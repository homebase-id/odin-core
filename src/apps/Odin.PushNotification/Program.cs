using FirebaseAdmin;
using FirebaseAdmin.Messaging;
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

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/ping", () => "pong");

app.MapPost("/message/v1", async (
        ILogger<PushNotification> logger,
        IPushNotification pushNotification,
        IValidator<DevicePushNotificationRequestV1> validator,
        [FromBody] DevicePushNotificationRequestV1 request) =>
    {
        var validationResult = validator.Validate(request);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(validationResult.Errors);
        }

        // SEB:TODO the sender signs the message using his private key from his SSL certificate.

        // homebase host:
        // request.Signature = encrypt(private_key_of(request.OriginDomain), request.Timestamp)

        // push service (this service):
        // cache public key of request.OriginDomain
        // timestamp = decrypt(public_key_of(request.OriginDomain), request.Signature)
        // test that timestamp equals request.Timestamp

        // The recipient then uses OriginDomain to look up the public key from the sender's SSL certificate
        // and verifies the signature.
        // public key: request.OriginDomain
        // signature: request.Signature

        try
        {
            var response = await pushNotification.Post(request);
            logger.LogInformation("Successfully sent message: {response}", response);
            return Results.Ok("Message sent successfully to Firebase.");
        }
        catch (FirebaseException e)
        {
            logger.LogError("Error sending message: {code} - {error}", e.ErrorCode, e.Message);
            return Results.Problem(type: e.ErrorCode.ToString(), detail: e.Message, statusCode: 502);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending message: {error}", e.Message);
            return Results.Problem("An internal error occurred.", statusCode: 500);
        }
    })
    .WithOpenApi();

app.Run();

//

public class PushNotificationRequestValidator : AbstractValidator<DevicePushNotificationRequestV1>
{
    public PushNotificationRequestValidator()
    {
        RuleFor(request => request.Version).Equal(1);
        RuleFor(request => request.DeviceToken).NotEmpty();
        RuleFor(request => request.OriginDomain).NotEmpty();
        RuleFor(request => request.Signature).NotEmpty();
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.CorrelationId).NotEmpty();
        RuleFor(request => request.Data).NotEmpty();
    }
}

//