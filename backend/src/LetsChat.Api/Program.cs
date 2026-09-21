using System.Threading.RateLimiting;
using LetsChat.Api.Endpoints;
using LetsChat.Api.Middleware;
using LetsChat.Application.Ports;
using LetsChat.Application.Services;
using LetsChat.Infrastructure.Identity;
using LetsChat.Infrastructure.Persistence;
using LetsChat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
builder.Services.AddSingleton<IEnvelopeRepository, PostgresEnvelopeRepository>();
builder.Services.AddSingleton<EnvelopeService>();
builder.Services.AddSingleton<IAccountRepository, PostgresAccountRepository>();
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<IRefreshTokenRepository, PostgresRefreshTokenRepository>();
builder.Services.AddSingleton<ITokenIssuer>(_ => new JwtTokenIssuer(
    pg("JWT_SIGNING_KEY"), issuer: "lets-chat", audience: "lets-chat-app"));
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<IDeviceRepository, PostgresDeviceRepository>();
builder.Services.AddSingleton<IContactRepository, PostgresContactRepository>();
builder.Services.AddSingleton<IDeviceSignatureVerifier, Ed25519SignatureVerifier>();
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<ContactService>();
builder.Services.AddSingleton<RelayAuthService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "lets-chat",
            ValidAudience = "lets-chat-app",
            IssuerSigningKey = new SymmetricSecurityKey(
                Convert.FromBase64String(pg("JWT_SIGNING_KEY"))),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

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
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.MapAuth();
app.MapDevices();
app.MapContacts();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "lets-chat-relay" }));
app.Map("/relay", app => app.UseMiddleware<RelayWebSocketMiddleware>());

app.Run();
