using MesXray.Api.Bootstrap;
using MesXray.Api.Configuration;
using MesXray.Api.Endpoints;
using MesXray.Api.Errors;
using MesXray.Domain.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => XRayJson.Apply(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<XRayExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddXRay(builder.Configuration);

var xray = builder.Configuration.GetSection(XRayOptions.SectionName).Get<XRayOptions>() ?? new XRayOptions();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(xray.CorsOrigins.ToArray())
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.MapOpenApi();
app.MapXRayEndpoints();

// Serve the built web UI from wwwroot when present (production-style single process).
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.BootstrapXRay();
app.Run();

/// <summary>Entry point marker so integration tests can host the API with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
