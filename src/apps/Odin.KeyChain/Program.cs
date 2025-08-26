using Odin.KeyChain;
using System.Collections.Concurrent;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.KeyChain;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    var databasePath = builder.Configuration.GetConnectionString("DatabasePath") ?? "blockchain.db";
    containerBuilder.AddSqliteKeyChainDatabaseServices(databasePath);
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

var app = builder.Build();

var db = app.Services.GetRequiredService<KeyChainDatabase>();
KeyChainDatabaseUtil.InitializeDatabaseAsync(db).Wait(); // Only do this once per boot

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
