using Dapper;
using Microsoft.Data.SqlClient;
using Payments.Dal.Interfaces;

namespace Payments.Dal.Repositories;

public sealed class ProcessedEventRepository : RepositoryBase, IProcessedEventRepository
{
    // 2627 — порушення PRIMARY KEY / UNIQUE constraint, 2601 — унікального індексу
    private static readonly int[] DuplicateKeyErrors = [2627, 2601];

    public ProcessedEventRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction) { }

    public async Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default)
    {
        const string sql = "INSERT dbo.ProcessedEvents (EventId, EventType) VALUES (@EventId, @EventType)";
        await EnsureOpenAsync(ct);
        try
        {
            await Connection.ExecuteAsync(new CommandDefinition(sql, new { EventId = eventId, EventType = eventType },
                Transaction, CommandTimeoutSeconds, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (DuplicateKeyErrors.Contains(ex.Number))
        {
            return false;   // уже оброблено: це дублікат
        }
    }
}
