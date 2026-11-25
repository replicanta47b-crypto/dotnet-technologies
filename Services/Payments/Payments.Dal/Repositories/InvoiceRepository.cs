using Dapper;
using Microsoft.Data.SqlClient;
using Payments.Dal.Interfaces;
using Payments.Domain.Models;

namespace Payments.Dal.Repositories;

/// <summary>Репозиторій локальної копії довідника рахунків (ADO.NET + Dapper, спільний CRUD з бази).</summary>
public sealed class InvoiceRepository : BaseDapperRepository<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.Invoices") { }

    // Id приходить із Білінгу (без IDENTITY), тому передається явно
    protected override string InsertSql => @"
        INSERT dbo.Invoices (Id, Number, MerchantName, Amount, Currency)
        VALUES (@Id, @Number, @MerchantName, @Amount, @Currency);
        SELECT @Id;";

    public async Task<Invoice?> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM dbo.Invoices WHERE Number = @Number";
        await EnsureOpenAsync(ct);
        return await Connection.QueryFirstOrDefaultAsync<Invoice>(Cmd(sql, new { Number = number }, ct));
    }

    public Task<bool> UpdateAsync(Invoice invoice, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        const string sql = @"UPDATE dbo.Invoices
                             SET Number = @Number, MerchantName = @MerchantName, Amount = @Amount,
                                 Currency = @Currency, UpdatedAt = SYSUTCDATETIME()
                             WHERE Id = @Id";
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Cmd(sql, invoice, ct)) > 0;
    });

    public Task<SnapshotResult> UpsertSnapshotAsync(Invoice snapshot, DateTime occurredAtUtc, CancellationToken ct = default)
        => TranslateAsync(async () =>
    {
        await EnsureOpenAsync(ct);

        // 1) оновити, якщо копія є і ця подія новіша за вже застосовану
        const string update = @"
            UPDATE dbo.Invoices
            SET Number = @Number, MerchantName = @MerchantName, Amount = @Amount, Currency = @Currency,
                UpdatedAt = @At, SyncedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND (UpdatedAt IS NULL OR UpdatedAt <= @At)";
        var p = new { snapshot.Id, snapshot.Number, snapshot.MerchantName, snapshot.Amount, snapshot.Currency, At = occurredAtUtc };
        if (await Connection.ExecuteAsync(Cmd(update, p, ct)) > 0)
            return SnapshotResult.Updated;

        // 2) копії ще немає — вставити; якщо є, але новіша (UpdatedAt > @At) — подія застаріла
        const string insert = @"
            INSERT dbo.Invoices (Id, Number, MerchantName, Amount, Currency, UpdatedAt)
            SELECT @Id, @Number, @MerchantName, @Amount, @Currency, @At
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Invoices WHERE Id = @Id)";
        return await Connection.ExecuteAsync(Cmd(insert, p, ct)) > 0 ? SnapshotResult.Inserted : SnapshotResult.Stale;
    });

    public Task<int> RenameMerchantAsync(string oldName, string newName, DateTime occurredAtUtc, CancellationToken ct = default)
        => TranslateAsync(async () =>
    {
        const string sql = @"
            UPDATE dbo.Invoices
            SET MerchantName = @NewName, UpdatedAt = @At, SyncedAt = SYSUTCDATETIME()
            WHERE MerchantName = @OldName AND (UpdatedAt IS NULL OR UpdatedAt <= @At)";
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Cmd(sql, new { OldName = oldName, NewName = newName, At = occurredAtUtc }, ct));
    });
}
