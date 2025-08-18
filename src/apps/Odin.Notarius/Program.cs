using Odin.Core.Cryptography.Data;
using Odin.Core;
using Odin.KeyChain;
using System.Collections.Concurrent;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Notary;
using Odin.Notarius;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    var databasePath = builder.Configuration.GetConnectionString("DatabasePath") ?? "notarychain.db";
    containerBuilder.AddSqliteNotaryDatabaseServices(databasePath);
    containerBuilder.AddDatabaseCacheServices();
    containerBuilder.AddDatabaseCounterServices();
});

builder.Services.AddSingleton<INodeLock, NodeLock>();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var pendingRegistrationsCache = new ConcurrentDictionary<string, PendingRegistrationData>();
builder.Services.AddSingleton<ConcurrentDictionary<string, PendingRegistrationData>>(pendingRegistrationsCache);

// ============== Get the Notary Public keyPair
SensitiveByteArray eccPwd = new SensitiveByteArray(new Guid("86d6e007-cf89-468c-acc5-66bfa14b9ce7").ToByteArray());
EccFullKeyData eccKey = EccKeyStorage.LoadKey(eccPwd);

builder.Services.AddSingleton<SensitiveByteArray>(eccPwd);
builder.Services.AddSingleton<EccFullKeyData>(eccKey);

var app = builder.Build();

var db = app.Services.GetRequiredService<NotaryDatabase>();
NotaryDatabaseUtil.InitializeDatabaseAsync(db).Wait();

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
