using Duende.IdentityServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.IdentityServer;
using Platform.IdentityServer.Data;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, health checks

// ---- окремий SQL Server (identityserver-sql) → БД IdentityServerDb; рядок підключення передає AppHost ----
var connectionString = builder.Configuration.GetConnectionString("IdentityServerDb")
    ?? throw new InvalidOperationException("Connection string 'IdentityServerDb' is not configured.");
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;   // міграції трьох контекстів лежать у цьому проєкті

// користувачі: ASP.NET Core Identity (пароль від 8 символів, lockout після 5 невдалих спроб)
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString, s => s.MigrationsAssembly(migrationsAssembly)));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddRazorPages();

// ---- Duende IdentityServer: конфігурація і operational-дані — в БД (EF), користувачі — через ASP.NET Identity ----
builder.Services.AddIdentityServer(o =>
    {
        o.IssuerUri = builder.Configuration["IdentityServer:Issuer"];
        o.Events.RaiseErrorEvents = true;
        o.Events.RaiseInformationEvents = true;
        o.Events.RaiseFailureEvents = true;
        o.Events.RaiseSuccessEvents = true;
    })
    .AddConfigurationStore(o =>
        o.ConfigureDbContext = b => b.UseSqlServer(connectionString, s => s.MigrationsAssembly(migrationsAssembly)))
    .AddOperationalStore(o =>
    {
        o.ConfigureDbContext = b => b.UseSqlServer(connectionString, s => s.MigrationsAssembly(migrationsAssembly));
        o.EnableTokenCleanup = true;            // прострочені grants/токени видаляються автоматично
    })
    .AddAspNetIdentity<ApplicationUser>();

builder.Services.AddHostedService<SeedHostedService>();   // міграції + seed на старті (не під час `dotnet ef`)

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();   // /.well-known/openid-configuration, /connect/authorize, /connect/token, /connect/userinfo, ...
app.UseAuthorization();
app.MapRazorPages().RequireAuthorization();   // Quickstart UI (сторінки з [AllowAnonymous] — login, logout, error — лишаються відкритими)
app.MapDefaultEndpoints();

app.Run();
