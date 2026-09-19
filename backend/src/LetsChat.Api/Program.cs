using LetsChat.Api.Middleware;
using LetsChat.Application.Ports;
using LetsChat.Application.Services;
using LetsChat.Infrastructure.EnvelopeStore;
using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Load repo-root .env before configuration is built (secrets live outside appsettings).
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Hexagonal wiring: ports -> adapters via DI (ADR-0001).
// Application services depend on interfaces; infrastructure registers them.
var pg = new Func<string, string>(name => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is required."));
var connectionString =
    $"Host={pg("POSTGRES_HOST")};Port={pg("POSTGRES_PORT")};" +
    $"Database={pg("POSTGRES_DB")};Username={pg("POSTGRES_USER")};Password={pg("POSTGRES_PASSWORD")}";
builder.Services.AddDbContextFactory<LetsChatDbContext>(
    options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IEnvelopeStore, PostgresEnvelopeStore>();
builder.Services.AddSingleton<EnvelopeService>();

var app = builder.Build();

app.UseWebSockets();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "lets-chat-relay" }));
app.Map("/relay", app => app.UseMiddleware<RelayWebSocketMiddleware>());

app.Run();
