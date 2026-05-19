using G4Composer.Server;
using G4Composer.Server.Examples;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Database (EF Core + SQLite dev / PostgreSQL prod) ────────────────────────
// SQLite lokalnie (plik g4composer.db w katalogu projektu), PostgreSQL na serwerze.
// Zmiana: appsettings.Production.json → ConnectionStrings:Default + UseNpgsql w kodzie.
builder.Services.AddDatabase(builder.Configuration, builder.Environment);

// ── Quadro (engines, validation, docker, job runner) ──────────────────────────
// Rejestracja całej warstwy Quadro w jednym miejscu — patrz QuadroServiceCollectionExtensions.
// Wersję silnika (14G / 14L / ...) przełącza się w appsettings.json → "Quadro:Version".
builder.Services.AddQuadro(builder.Configuration);

// ── Pipeline (ViennaRNA + gqrs + Quadro 3D pipeline) ─────────────────────────
builder.Services.AddSingleton<G4Composer.Server.Services.IRnaSecondaryStructureService,
                               G4Composer.Server.Services.RnaSecondaryStructureService>();
builder.Services.AddSingleton<G4Composer.Server.Services.IGqrsService,
                               G4Composer.Server.Services.GqrsService>();
builder.Services.AddSingleton<G4Composer.Server.Services.IPipelinePdbStore,
                               G4Composer.Server.Services.InMemoryPipelinePdbStore>();

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title       = "G4Composer API",
        Version     = "v1",
        Description = "API do obliczeń struktury G-kwadrupleksu za pomocą kontenera Docker Quadro (CYANA/Xplor-NIH). " +
                      "Aktywna wersja silnika konfigurowana przez sekcję 'Quadro' w appsettings.json.",
        Contact     = new() { Name = "G4Composer" },
    });

    // Przykładowe dane wejściowe z QuadroInputListExample
    options.ExampleFilters();

    // Obsługa XML-docstrings (wymaga <GenerateDocumentationFile> w .csproj)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);

    // Dodaj nagłówek Content-Type: chemical/x-pdb jako dozwolony typ odpowiedzi
    options.MapType<FileContentResult>(() => new OpenApiSchema()
    {
        Type = JsonSchemaType.String,
        Format = "binary",
    });
});

// Rejestracja przykładów
builder.Services.AddSwaggerExamplesFromAssemblyOf<QuadroInputListExample>();

// ── CORS (Vite dev server na porcie 5173) ─────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("ViteDev", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("X-Job-Id", "X-Atom-Count", "Content-Disposition",
                "X-Std-Energy", "X-Alt-Energy", "X-Has-Alt", "X-Winner");
    });
});

// ── appsettings defaults ──────────────────────────────────────────────────────
builder.Services.Configure<RouteOptions>(opt => opt.LowercaseUrls = true);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();
// ─────────────────────────────────────────────────────────────────────────────

// Apply migrations + seed on startup (safe to call every time — skips if up to date).
await app.InitialiseDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    // Swagger JSON: /openapi/v1.json
    app.UseSwagger(c => c.RouteTemplate = "openapi/{documentName}.json");

    // Scalar UI: /scalar/v1
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("G4Composer API Reference")
               .WithTheme(ScalarTheme.DeepSpace)
               .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Fetch)
               .WithOpenApiRoutePattern("/openapi/v1.json");
    });

    // Swagger UI klasyczny (opcjonalnie): /swagger
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "G4Composer v1"));
}

// Middleware
app.UseCors("ViteDev");
app.UseHttpsRedirection();
app.UseStaticFiles(); // Serwuj build React z wwwroot
app.UseAuthorization();
app.MapControllers();

// SPA fallback – zwracaj index.html dla nieznanych tras (React Router)
app.MapFallbackToFile("index.html");

app.Run();
