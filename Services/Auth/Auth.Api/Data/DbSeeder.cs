using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Security;

namespace Auth.Api.Data;

/// <summary>Старт: міграції, ролі User/Admin, тестові користувачі. Паролі — лише для локальної розробки (appsettings → Seed).</summary>
public static class DbSeeder
{
    public static readonly string[] Roles = ["User", "Admin"];

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
        var db = services.GetRequiredService<ApplicationDbContext>();

        // SQL Server у контейнері може ще прокидатись — кілька спроб
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
                else await db.Database.EnsureCreatedAsync();   // міграцій ще немає (див. scripts/generate-migrations.ps1)
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "IdentityDb ще недоступна (спроба {Attempt}/10)", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var config = services.GetRequiredService<IConfiguration>().GetSection("Seed");
        await EnsureUserAsync(services, config["AdminUserName"]!, config["AdminEmail"]!, config["AdminPassword"]!, ["Admin", "User"], logger);
        await EnsureUserAsync(services, config["UserUserName"]!, config["UserEmail"]!, config["UserPassword"]!, ["User"], logger);
    }

    private static async Task EnsureUserAsync(IServiceProvider services, string userName, string email, string password, string[] roles, ILogger logger)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByNameAsync(userName) is not null) return;

        var user = new ApplicationUser { UserName = userName, Email = email, EmailConfirmed = true, FullName = userName };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Seed користувача {User} не вдався: {Errors}", userName, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRolesAsync(user, roles);
        logger.LogInformation("Seed: користувач {User} ({Roles})", userName, string.Join(", ", roles));
    }
}
