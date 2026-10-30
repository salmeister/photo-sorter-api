using ImageMagick;
using ImageMagick.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using PhotoSorterAPI.Models;
using PhotoSorterAPI.Services;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Serialization;

// Lock down ImageMagick before any image is read: files in the import folders are untrusted input.
var magickConfig = ConfigurationFiles.Default;
magickConfig.Policy.Data = """
<policymap>
  <policy domain="delegate" rights="none" pattern="*" />
  <policy domain="path" rights="none" pattern="@*" />
  <policy domain="coder" rights="none" pattern="{EPHEMERAL,URL,HTTP,HTTPS,FTP,MVG,MSL,TEXT,TXT,LABEL,CAPTION,PANGO,PS,PS2,PS3,EPS,EPI,EPSI,EPSF,PDF,PDFA,XPS,PCL,SVG,MSVG,RSVG,SHOW,WIN,X,SCREENSHOT,INLINE}" />
  <policy domain="resource" name="width" value="64KP" />
  <policy domain="resource" name="height" value="64KP" />
</policymap>
""";
MagickNET.Initialize(magickConfig);

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
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Photo Sorter API",
        Description = "API to discover pictures and videos in the configured source locations then rename, rotate, and move based on the configured settings."
    });
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
    options.EnableAnnotations(); // This method is available in Swashbuckle.AspNetCore.Annotations
});


var app = builder.Build();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
