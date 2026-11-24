namespace Disputes.Infrastructure.Seeding;

/// <summary>Наповнення колекції початковими даними. Реалізація зобов'язана бути ідемпотентною.</summary>
public interface IDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
