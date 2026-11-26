using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Payments.Api.Infrastructure;

/// <summary>
/// Ідемпотентна ініціалізація PaymentsDb у контейнері SQL Server (його запускає AppHost, база стартує порожньою):
/// db/p1/schema.sql → procedures.sql → seed.sql. Скрипти з Лаб. 01 копіюються в вихідну папку (див. csproj),
/// діляться на пакети за рядком GO й виконуються по черзі. Вмикається налаштуванням Database:InitializeOnStartup.
/// </summary>
public static partial class DatabaseInitializer
{
    private static readonly string[] Scripts = ["schema.sql", "procedures.sql", "seed.sql"];

    public static async Task InitializeAsync(string connectionString, string scriptsDirectory, ILogger logger, CancellationToken ct = default)
    {
        await EnsureDatabaseExistsAsync(connectionString, logger, ct);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        foreach (var name in Scripts)
        {
            var path = Path.Combine(scriptsDirectory, name);
            var text = await File.ReadAllTextAsync(path, ct);

            var batches = GoSeparator().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
            foreach (var batch in batches)
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                await command.ExecuteNonQueryAsync(ct);
            }

            logger.LogInformation("Database script {Script} applied ({Batches} batches)", name, batches.Count);
        }
    }

    /// <summary>
    /// Лаб. 10: під Aspire (F5) базу PaymentsDb створює AppHost, а в Kubernetes (aspirate / ручні маніфести) на SQL Server її ніхто не створює —
    /// тому сервіс сам створює базу, якщо її немає (підключення до master, ідемпотентно). Помилка входу 4060 («Cannot open database») зникає.
    /// </summary>
    private static async Task EnsureDatabaseExistsAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database)) return;

        builder.InitialCatalog = "master";
        // SQL Server у кластері може ще стартувати: кілька спроб з паузою, далі — помилка (Kubernetes перезапустить под)
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var master = new SqlConnection(builder.ConnectionString);
                await master.OpenAsync(ct);
                await using var command = new SqlCommand(
                    $"IF DB_ID(@name) IS NULL CREATE DATABASE [{database.Replace("]", "]]")}]", master) { CommandTimeout = 60 };
                command.Parameters.AddWithValue("@name", database);
                await command.ExecuteNonQueryAsync(ct);
                logger.LogInformation("Database {Database} is ready", database);
                return;
            }
            catch (SqlException ex) when (attempt < 10)
            {
                logger.LogWarning("SQL Server is not ready yet (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
