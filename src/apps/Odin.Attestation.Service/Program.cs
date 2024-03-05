using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.AttestationDatabase;
using OdinsAttestation;

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

var _db = new AttestationDatabase(@"Data Source=attestation.db");
AttestationDatabaseUtil.InitializeDatabase(_db); // Only do this once per boot

builder.Services.AddSingleton<AttestationDatabase>(_db);
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
