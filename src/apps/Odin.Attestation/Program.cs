using Autofac;
using Autofac.Extensions.DependencyInjection;
using Odin.Attestation;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Attestation;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    var databasePath = builder.Configuration.GetConnectionString("DatabasePath") ?? "attestation.db";
    containerBuilder.AddSqliteAttestationDatabaseServices(databasePath);
    containerBuilder.AddDatabaseCacheServices();
    containerBuilder.AddDatabaseCounterServices();
});

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ============== Initialize
SensitiveByteArray eccPwd = new SensitiveByteArray(new Guid("86d6e007-cf89-468c-acc5-66bfa14b9ce7").ToByteArray());
EccFullKeyData eccKey = EccKeyStorage.LoadKey(eccPwd);

builder.Services.AddSingleton<SensitiveByteArray>(eccPwd);
builder.Services.AddSingleton<EccFullKeyData>(eccKey);

var app = builder.Build();

var db = app.Services.GetRequiredService<AttestationDatabase>();
AttestationDatabaseUtil.InitializeDatabaseAsync(db).Wait(); // Only do this once per boot


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

// app.UseAuthorization();

app.MapControllers();

app.Run();
