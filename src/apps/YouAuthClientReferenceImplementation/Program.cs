using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Odin.Core.Http;
using YouAuthClientReferenceImplementation;

const int tcpPort = 7280;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(tcpPort, listenOptions =>
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

        var certPem = File.ReadAllText(Path.Combine(assemblyDirectory!, "../../../../Odin.Hosting/https/thirdparty.dotyou.cloud/certificate.crt"));
        var keyPem = File.ReadAllText(Path.Combine(assemblyDirectory!, "../../../../Odin.Hosting/https/thirdparty.dotyou.cloud/private.key"));
        var x509 = X509Certificate2.CreateFromPem(certPem, keyPem);
        listenOptions.UseHttps(x509);
    });
});

builder.Services.AddSingleton<ConcurrentDictionary<string, State>>();
builder.Services.AddSingleton<IDynamicHttpClientFactory, DynamicHttpClientFactory>();

var app = builder.Build();
app.UseMiddleware<LoggingMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"click-click-click: https://thirdparty.dotyou.cloud:{tcpPort}/");
});

app.Run();
