using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
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

        var certPem = File.ReadAllText(Path.Combine(assemblyDirectory!, "certificate.crt"));
        var keyPem = File.ReadAllText(Path.Combine(assemblyDirectory!, "private.key"));
        using var temp = X509Certificate2.CreateFromPem(certPem, keyPem);
        var x509 = new X509Certificate2(temp.Export(X509ContentType.Pfx));
        listenOptions.UseHttps(x509);
    });
});

builder.Services.AddSingleton<ConcurrentDictionary<string, State>>();
builder.Services.AddHttpClient("default")
    .ConfigureHttpClient(c =>
    {
        // this is called everytime you request a httpclient
        c.Timeout = TimeSpan.FromSeconds(5);
    })
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
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"click-click-click: https://thirdparty.dotyou.cloud:{tcpPort}/");
});

app.Run();
