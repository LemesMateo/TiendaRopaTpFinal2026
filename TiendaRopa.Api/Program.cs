using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TiendaRopa.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// ---- Servicios ----
builder.Services.AddProblemDetails();
builder.Services.AddControllers();

// Documentación OpenAPI (/openapi/v1.json) + UI Scalar (/scalar/v1)
builder.Services.AddOpenApi();

builder.Services.AddDbContext<VelvetRoseDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("VelvetRose"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

var app = builder.Build();

// ---- Pipeline ----
if (app.Environment.IsDevelopment())
{
    // OpenAPI + UI de pruebas
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Migración y datos de desarrollo (sólo Development).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<VelvetRoseDbContext>();
    await DevelopmentSeeder.SeedAsync(db);
}

app.MapControllers();

app.Run();
