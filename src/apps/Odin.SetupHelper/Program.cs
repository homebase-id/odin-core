using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.CorrelationId.Serilog;
using Odin.SetupHelper;
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
// Misc
//

builder.Services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
builder.Services.AddSingleton<ICorrelationContext, CorrelationContext>();

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.MapGet("/ping", () => "pong");

app.Run();

//

