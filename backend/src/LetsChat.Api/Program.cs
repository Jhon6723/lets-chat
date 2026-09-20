using System.Threading.RateLimiting;
using LetsChat.Api.Endpoints;
using LetsChat.Api.Middleware;
using LetsChat.Application.Ports;
using LetsChat.Application.Services;
using LetsChat.Infrastructure.AccountStore;
using LetsChat.Infrastructure.EnvelopeStore;
using LetsChat.Infrastructure.Identity;
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
builder.Services.AddSingleton<IAccountStore, PostgresAccountStore>();
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<AccountService>();

// Auth endpoints get a tight per-IP window: credential stuffing resistance
// (ADR-0007). 10 req/min per IP; bursts get 429, not queued.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseRateLimiter();
app.UseWebSockets();
app.MapAuth();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "lets-chat-relay" }));
app.Map("/relay", app => app.UseMiddleware<RelayWebSocketMiddleware>());

app.Run();
