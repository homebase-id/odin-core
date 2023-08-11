using Microsoft.AspNetCore.Mvc;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.KeyChain;
using OdinsChains.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var _db = new KeyChainDatabase(@"Data Source=blockchain.db");
KeyChainDatabaseUtil.InitializeDatabase(_db); // Only do this once per boot

builder.Services.AddSingleton<KeyChainDatabase>(_db);

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
