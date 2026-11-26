using Auth.Api.Authorization;
using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Security;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, health checks, service discovery

// ---- IdentityDb: окрема БД (ресурс AppHost → ConnectionStrings__IdentityDb) ----
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityDb' is not configured.");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));

// ---- ASP.NET Core Identity: пароль від 8 символів, lockout після 5 невдалих спроб, унікальний email ----
builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = true;

        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();   // токени підтвердження email і скидання пароля

// ---- JWT: Auth.Api сам видає токени (HS256) і валідує їх тією ж схемою Platform.Security, що й решта сервісів ----
builder.AddPlatformAuthentication("auth");
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<TokenIssuer>();
builder.Services.AddSingleton<IEmailService, MockEmailService>();   // mock (за завданням): лист → лог і /auth/dev/outbox

var authServiceIssuer = builder.Configuration["Auth:AuthService:Issuer"] ?? new PlatformAuthOptions().AuthService.Issuer;
builder.Services.AddSingleton<IAuthorizationHandler, OwnerOrAdminHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p
        .RequireAuthenticatedUser()
        .RequireRole("Admin")
        .RequireAssertion(ctx => ctx.User.FindFirst("iss")?.Value == authServiceIssuer))   // роль Admin — лише з токена Auth Service
    .AddPolicy(Policies.OwnerOrAdmin, p => p.RequireAuthenticatedUser().AddRequirements(new OwnerOrAdminRequirement()));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.AddPlatformSecurity(builder.Configuration, "auth"));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.UsePlatformAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapDefaultEndpoints();

// міграції + ролі User/Admin + тестові користувачі
using (var scope = app.Services.CreateScope())
    await DbSeeder.InitializeAsync(scope.ServiceProvider);

app.Run();
