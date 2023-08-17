using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ThirdPartyApp;
using ThirdPartyApp.Pages;

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

        var certPem = File.ReadAllText(Path.Combine(assemblyDirectory!, "certificate.crt"));
        var keyPem = File.ReadAllText(Path.Combine(assemblyDirectory!, "private.key"));
        using var temp = X509Certificate2.CreateFromPem(certPem, keyPem);
        var x509 = new X509Certificate2(temp.Export(X509ContentType.Pfx));
        listenOptions.UseHttps(x509);
    });
});

builder.Services.AddSingleton<ConcurrentDictionary<string, State>>();
builder.Services.AddHttpClient("default")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

var app = builder.Build();
app.UseMiddleware<LoggingMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"click-click-click: https://thirdparty.dotyou.cloud:{tcpPort}/");
});


app.Run();
