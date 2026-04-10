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

//
// Firebase Messaging
//

var firebaseCredentialsFile = builder.Configuration["Firebase:CredentialsFileName"] ?? "";
builder.Services.AddSingleton<IPushNotification>(new PushNotification(firebaseCredentialsFile));

//
// Misc
//

builder.Services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
builder.Services.AddSingleton<ICorrelationContext, CorrelationContext>();
builder.Services.AddSingleton<ISignatureCheck, SignatureCheck>();
builder.Services.AddFluentValidationAutoValidation()
    .AddValidatorsFromAssemblyContaining<PushNotificationRequestValidator>();

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.MapGet("/ping", () => "pong");

app.MapPost("/message/v1", async (
        ILogger<PushNotification> logger,
        IPushNotification pushNotification,
        ISignatureCheck signatureCheck,
        IValidator<DevicePushNotificationRequestV1> validator,
        [FromBody] DevicePushNotificationRequestV1 request) =>
    {
        logger.LogInformation(
            "Received push request id {id} platform {platform} from {from} to {to} origin {origin} device {device}",
            request.Id, request.DevicePlatform, request.FromDomain, request.ToDomain, request.OriginDomain,
            request.DeviceToken);

        var validationResult = validator.Validate(request);
        if (!validationResult.IsValid)
        {
            logger.LogWarning(
                "Validation failed for push request {id} from {from}: {errors}",
                request.Id, request.FromDomain,
                string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
            return Results.BadRequest(validationResult.Errors);
        }

        //
        // Check signature (unless it's from *.dotyou.cloud in development mode)
        //
        var skipSignatureCheck = app.Environment.IsDevelopment() && request.OriginDomain.EndsWith(".dotyou.cloud");
        if (!skipSignatureCheck)
        {
            var isValidSignature = await signatureCheck.Validate(request.OriginDomain, request.Signature, request.Id);
            if (!isValidSignature)
            {
                logger.LogWarning(
                    "Invalid signature on push request {id} from origin {origin}",
                    request.Id, request.OriginDomain);
                return Results.BadRequest("Invalid message signature");
            }
        }
        else
        {
            logger.LogDebug(
                "Skipping signature check for development origin {origin} on request {id}",
                request.OriginDomain, request.Id);
        }

        //
        // Send the message
        //
        try
        {
            var response = await pushNotification.Post(request);
            logger.LogInformation("Successfully sent {platform} message with id {id} from {from} to {to} for device {device}: {response}",
                request.DevicePlatform, request.Id, request.FromDomain, request.ToDomain, request.DeviceToken, response);
            return Results.Ok("Message sent successfully to Firebase.");
        }
        catch (FirebaseMessagingException e)
        {
            var httpStatus = e.HttpResponse?.StatusCode.ToString() ?? "n/a";
            string? httpBody = null;
            if (e.HttpResponse?.Content != null)
            {
                try
                {
                    httpBody = await e.HttpResponse.Content.ReadAsStringAsync();
                }
                catch (Exception readEx)
                {
                    logger.LogDebug(readEx, "Could not read FCM response body for request {id}", request.Id);
                }
            }

            logger.LogError(e,
                "Firebase send failed for {platform} message {id} from {from} to {to} device {device}: " +
                "errorCode={errorCode} messagingErrorCode={messagingErrorCode} fcmStatus={fcmStatus} fcmBody={fcmBody} message={message}",
                request.DevicePlatform, request.Id, request.FromDomain, request.ToDomain, request.DeviceToken,
                e.ErrorCode, e.MessagingErrorCode, httpStatus, httpBody, e.Message);

            return Results.Problem(type: e.ErrorCode.ToString(), detail: e.Message, statusCode: 502);
        }
        catch (FirebaseException e)
        {
            logger.LogError(e,
                "Firebase send failed (non-messaging) for {platform} message {id} from {from} to {to} device {device}: " +
                "errorCode={errorCode} message={message}",
                request.DevicePlatform, request.Id, request.FromDomain, request.ToDomain, request.DeviceToken,
                e.ErrorCode, e.Message);
            return Results.Problem(type: e.ErrorCode.ToString(), detail: e.Message, statusCode: 502);
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "Unexpected error sending {platform} message {id} from {from} to {to} for device {device}: {error}",
                request.DevicePlatform, request.Id, request.FromDomain, request.ToDomain, request.DeviceToken, e.Message);
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
        RuleFor(request => request.DevicePlatform).NotEmpty();
        RuleFor(request => request.OriginDomain).NotEmpty();
        RuleFor(request => request.Signature).NotEmpty();
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.CorrelationId).NotEmpty();
        RuleFor(request => request.Data).NotEmpty();
        RuleFor(request => request.Title).NotEmpty();
        RuleFor(request => request.Body).NotEmpty();
        RuleFor(request => request.FromDomain).NotEmpty();
        RuleFor(request => request.ToDomain).NotEmpty();
    }
}

//

