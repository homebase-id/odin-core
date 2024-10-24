using Odin.Core.Cryptography.Data;
using Odin.Core;
using Odin.Core.Storage.SQLite.NotaryDatabase;
using Odin.KeyChain;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

using var db = new NotaryDatabase("notarychain.db");
using (var conn = db.CreateDisposableConnection())
{
    NotaryDatabaseUtil.InitializeDatabaseAsync(db, conn).Wait(); // Only do this once per boot
}

builder.Services.AddSingleton((NotaryDatabase)db);

var pendingRegistrationsCache = new ConcurrentDictionary<string, PendingRegistrationData>();
builder.Services.AddSingleton<ConcurrentDictionary<string, PendingRegistrationData>>(pendingRegistrationsCache);

// ============== Get the Notary Public keyPair
SensitiveByteArray eccPwd = new SensitiveByteArray(new Guid("86d6e007-cf89-468c-acc5-66bfa14b9ce7").ToByteArray());
EccFullKeyData eccKey = EccKeyStorage.LoadKey(eccPwd);

builder.Services.AddSingleton<SensitiveByteArray>(eccPwd);
builder.Services.AddSingleton<EccFullKeyData>(eccKey);

var app = builder.Build();

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
