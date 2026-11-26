using Platform.Security;
using Platform.ServiceDefaults;
using Serilog;
using Settlements.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                       // Serilog, OpenTelemetry, health checks, service discovery
builder.AddPlatformAuthentication("settlements");   // JWT від Auth Service / IdentityServer / Keycloak; audience settlements_api / settlements-api

builder.Services.AddSingleton<SettlementStore>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.AddPlatformSecurity(builder.Configuration, "settlements"));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.OAuthClientId("swagger");   // публічний client IdentityServer (Authorization Code + PKCE)
    o.OAuthUsePkce();
});

app.UsePlatformAuthorization();   // UseAuthentication + UseAuthorization

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapDefaultEndpoints();        // /health, /health/ready, ... — відкриті, без токена

app.Run();
