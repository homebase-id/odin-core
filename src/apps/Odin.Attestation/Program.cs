using Odin.Attestation;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.AttestationDatabase;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ============== Initialize
SensitiveByteArray eccPwd = new SensitiveByteArray(new Guid("86d6e007-cf89-468c-acc5-66bfa14b9ce7").ToByteArray());
EccFullKeyData eccKey = EccKeyStorage.LoadKey(eccPwd);

var db = new AttestationDatabase(@"attestation.db");
using (var conn = db.CreateDisposableConnection())
{
    AttestationDatabaseUtil.InitializeDatabaseAsync(db, conn).Wait(); // Only do this once per boot
}

builder.Services.AddSingleton<AttestationDatabase>(db);
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
