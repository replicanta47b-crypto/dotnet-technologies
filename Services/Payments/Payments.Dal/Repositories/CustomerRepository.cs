using System.Data;
using Microsoft.Data.SqlClient;
using Payments.Dal.Interfaces;
using Payments.Domain.Models;

namespace Payments.Dal.Repositories;

/// <summary>
/// Репозиторій платників на ЧИСТОМУ ADO.NET (Microsoft.Data.SqlClient): SqlCommand, параметри,
/// ручне читання через SqlDataReader. Усі значення передаються лише як параметри.
/// </summary>
public sealed class CustomerRepository : RepositoryBase, ICustomerRepository
{
    private const string Columns =
        "Id, Email, FullName, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    public CustomerRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction) { }

    public async Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = $"SELECT {Columns} FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0";
        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        const string sql = $"SELECT {Columns} FROM dbo.Customers WHERE Email = @Email AND IsDeleted = 0";
        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Customer>> GetPageAsync(int offset, int take, CancellationToken ct = default)
    {
        const string sql = $@"SELECT {Columns} FROM dbo.Customers WHERE IsDeleted = 0
                              ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY";
        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        command.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Customer>();
        while (await reader.ReadAsync(ct))
            result.Add(Map(reader));
        return result;
    }

    public Task<long> AddAsync(Customer customer, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        const string sql = @"INSERT dbo.Customers (Email, FullName, CreatedBy)
                             OUTPUT INSERTED.Id
                             VALUES (@Email, @FullName, @CreatedBy)";
        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 100).Value = (object?)customer.CreatedBy ?? DBNull.Value;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    });

    public Task<bool> UpdateAsync(Customer customer, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        const string sql = @"UPDATE dbo.Customers
                             SET Email = @Email, FullName = @FullName,
                                 UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
                             WHERE Id = @Id AND IsDeleted = 0";
        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = customer.Id;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = (object?)customer.UpdatedBy ?? DBNull.Value;
        return await command.ExecuteNonQueryAsync(ct) > 0;
    });

    public Task<bool> SoftDeleteAsync(long id, string? deletedBy, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        const string sql = @"UPDATE dbo.Customers
                             SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @DeletedBy
                             WHERE Id = @Id AND IsDeleted = 0";
        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@DeletedBy", SqlDbType.NVarChar, 100).Value = (object?)deletedBy ?? DBNull.Value;
        return await command.ExecuteNonQueryAsync(ct) > 0;
    });

    // команда прив'язується до спільного з'єднання й поточної транзакції Unit of Work
    private SqlCommand CreateCommand(string sql) =>
        new(sql, Connection, Transaction) { CommandTimeout = CommandTimeoutSeconds };

    private static Customer Map(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Email = r.GetString(1),
        FullName = r.GetString(2),
        CreatedAt = r.GetDateTime(3),
        CreatedBy = r.IsDBNull(4) ? null : r.GetString(4),
        UpdatedAt = r.IsDBNull(5) ? null : r.GetDateTime(5),
        UpdatedBy = r.IsDBNull(6) ? null : r.GetString(6),
        IsDeleted = r.GetBoolean(7),
        RowVersion = (byte[])r.GetValue(8)
    };
}
