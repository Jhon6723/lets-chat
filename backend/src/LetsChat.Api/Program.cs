using LetsChat.Api.Middleware;
using LetsChat.Application.Ports;
using LetsChat.Application.Services;
using LetsChat.Infrastructure.EnvelopeStore;

var builder = WebApplication.CreateBuilder(args);

// Hexagonal wiring: ports -> adapters via DI (ADR-0001).
// Application services depend on interfaces; infrastructure registers them.
builder.Services.AddSingleton<IEnvelopeStore, InMemoryEnvelopeStore>();
builder.Services.AddSingleton<EnvelopeService>();

var app = builder.Build();

app.UseWebSockets();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "lets-chat-relay" }));
app.Map("/relay", app => app.UseMiddleware<RelayWebSocketMiddleware>());

app.Run();
