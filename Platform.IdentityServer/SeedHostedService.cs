using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.IdentityServer.Data;

namespace Platform.IdentityServer;

/// <summary>
/// На старті: міграції трьох контекстів (PersistedGrantDbContext, ConfigurationDbContext, ApplicationDbContext) і seed.
/// Робиться в IHostedService, а не в Program.cs: `dotnet ef` будує хост, але не запускає його — seed під час міграцій не виконується.
/// Конфігурація (clients, resources, scopes) синхронізується з Config.cs при КОЖНОМУ старті.
/// </summary>
public sealed class SeedHostedService(IServiceProvider services, ILogger<SeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        // SQL Server у контейнері може ще прокидатись — кілька спроб
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await EnsureSchemaAsync(sp.GetRequiredService<PersistedGrantDbContext>(), ct);
                await EnsureSchemaAsync(sp.GetRequiredService<ConfigurationDbContext>(), ct);
                await EnsureSchemaAsync(sp.GetRequiredService<ApplicationDbContext>(), ct);
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "IdentityServerDb ще недоступна (спроба {Attempt}/10)", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        await SeedConfigurationAsync(sp.GetRequiredService<ConfigurationDbContext>(), sp.GetRequiredService<IConfiguration>(), ct);
        await SeedUsersAsync(sp.GetRequiredService<UserManager<ApplicationUser>>(), sp.GetRequiredService<IConfiguration>());
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task EnsureSchemaAsync(DbContext db, CancellationToken ct)
    {
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(ct);   // штатний шлях: міграції з репозиторію (scripts/generate-migrations.ps1)
            return;
        }

        // запасний шлях, якщо міграції ще не згенеровано: три контексти ділять одну БД, тож EnsureCreated не годиться
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(ct)) await creator.CreateAsync(ct);
        try { await creator.CreateTablesAsync(ct); }
        catch (Exception) { /* таблиці цього контексту вже існують */ }
    }

    private async Task SeedConfigurationAsync(ConfigurationDbContext db, IConfiguration config, CancellationToken ct)
    {
        db.Clients.RemoveRange(await db.Clients.ToListAsync(ct));
        db.ApiResources.RemoveRange(await db.ApiResources.ToListAsync(ct));
        db.ApiScopes.RemoveRange(await db.ApiScopes.ToListAsync(ct));
        db.IdentityResources.RemoveRange(await db.IdentityResources.ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        foreach (var c in Config.Clients(config)) db.Clients.Add(c.ToEntity());
        foreach (var r in Config.IdentityResources) db.IdentityResources.Add(r.ToEntity());
        foreach (var s in Config.ApiScopes) db.ApiScopes.Add(s.ToEntity());
        foreach (var r in Config.ApiResources) db.ApiResources.Add(r.ToEntity());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("IdentityServer: clients/resources/scopes синхронізовано з Config.cs");
    }

    private async Task SeedUsersAsync(UserManager<ApplicationUser> users, IConfiguration config)
    {
        foreach (var u in config.GetSection("Seed:Users").GetChildren())
        {
            var name = u["UserName"]!;
            if (await users.FindByNameAsync(name) is not null) continue;

            var result = await users.CreateAsync(new ApplicationUser { UserName = name, Email = u["Email"], EmailConfirmed = true }, u["Password"]!);
            logger.LogInformation("Seed користувача {User}: {Result}", name, result.Succeeded ? "ok" : string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
