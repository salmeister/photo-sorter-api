using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PhotoSorterAPI.Models;
using PhotoSorterAPI.Services;
using System.Configuration;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.Configure<AppConfigs>(builder.Configuration.GetSection("AppConfigs"));
builder.Services.AddScoped<IPhotoSorterService, PhotoSorterService>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

