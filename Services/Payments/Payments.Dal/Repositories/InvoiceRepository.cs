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
}
