using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Platform.IdentityServer.Data;

/// <summary>Користувач IdentityServer (власний набір таблиць AspNet* у IdentityServerDb — незалежний від Auth Service).</summary>
public class ApplicationUser : IdentityUser;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options);
