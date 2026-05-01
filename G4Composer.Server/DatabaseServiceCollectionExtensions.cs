using Microsoft.EntityFrameworkCore;
using G4Composer.Server.Data;

namespace G4Composer.Server;

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Data Source=g4composer.db";

        if (environment.IsProduction())
        {
            // PostgreSQL on the server — switch the NuGet package to Npgsql.EntityFrameworkCore.PostgreSQL
            // and change the connection string in appsettings.Production.json.
            // Code below stays identical — only the provider changes.
            services.AddDbContext<AppDbContext>(opt =>
                opt.UseNpgsql(connectionString));
        }
        else
        {
            // SQLite for local development — zero config, single file in project root.
            services.AddDbContext<AppDbContext>(opt =>
                opt.UseSqlite(connectionString));
        }

        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations and seeds the database.
    /// Call once at startup after app.Build().
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Applies any unapplied migrations (creates the file on first run).
        await db.Database.MigrateAsync();

        // Seeds data if tables are empty.
        await DatabaseSeeder.SeedAsync(db);
    }
}
