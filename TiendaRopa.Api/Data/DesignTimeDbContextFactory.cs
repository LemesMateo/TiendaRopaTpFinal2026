using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TiendaRopa.Api.Data;

/// <summary>
/// Permite a las herramientas de EF Core (migraciones) crear el contexto
/// sin levantar la aplicación. Lee la cadena de configuración de:
/// variable de entorno "ConnectionStrings__VelvetRose" o appsettings.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VelvetRoseDbContext>
{
    public VelvetRoseDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var connectionString = configuration.GetConnectionString("VelvetRose")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=VelvetRoseDev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        var options = new DbContextOptionsBuilder<VelvetRoseDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new VelvetRoseDbContext(options);
    }
}
