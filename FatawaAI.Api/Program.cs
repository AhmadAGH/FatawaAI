using FatawaAI.Core;
using FatawaAI.Domain;
using FatawaAI.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Controllers + API explorer + Swagger/OpenAPI.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// CORS: Allow UI project to access API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()  // Use AllowAnyOrigin() not WithOrigins("*")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Options
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama"));

// MediatR: register handlers from the Domain assembly.
builder.Services.AddMediatR(typeof(SearchFatwasQueryHandler).Assembly);

// PostgreSQL repositories.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
                       ?? "Host=localhost;Port=5432;Database=fatawa;Username=postgres;Password=postgres";
builder.Services.AddSingleton<IFatwaReadRepository>(
    _ => new FatwaReadRepository(connectionString));

// Ollama-based AI services via HttpClientFactory.
builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
builder.Services.AddHttpClient<IQueryAnalysisService, OllamaQueryAnalysisService>();
builder.Services.AddHttpClient<ISemanticFilterService, OllamaSemanticFilterService>();
builder.Services.AddHttpClient<IRerankingService, OllamaRerankingService>();

// Rate limiting: 50 req/min per IP for /api/search (can be refined later).
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("search", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit = 50;
        limiterOptions.QueueLimit = 0;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// app.UseHttpsRedirection(); // Commented out to avoid CORS issues in development

app.UseRouting();

// Enable CORS - must be after UseRouting and before UseAuthorization
app.UseCors();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();
