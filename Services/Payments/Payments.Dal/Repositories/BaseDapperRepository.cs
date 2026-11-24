using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Payments.Dal.Interfaces;

namespace Payments.Dal.Repositories;

/// <summary>
/// Спільний CRUD на ADO.NET + Dapper, написаний один раз. Конкретні репозиторії додають лише специфічні запити.
/// Ім'я таблиці — константа з коду (не вхідні дані користувача), тому його підстановка в текст запиту безпечна;
/// усі значення передаються тільки параметрами.
/// </summary>
public abstract class BaseDapperRepository<T> : RepositoryBase, IGenericRepository<T> where T : class
{
    private readonly string _table;
    private readonly bool _softDelete;

    protected BaseDapperRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction,
                                   string tableName, bool softDelete = false)
        : base(connection, currentTransaction)
    {
        _table = tableName;
        _softDelete = softDelete;
    }

    /// <summary>SQL вставки, що повертає новий Id скалярним значенням.</summary>
    protected virtual string InsertSql =>
        throw new NotSupportedException($"{GetType().Name} does not define a generic INSERT.");

    /// <summary>Команда Dapper, прив'язана до поточної транзакції Unit of Work.</summary>
    protected CommandDefinition Cmd(string sql, object? parameters = null, CancellationToken ct = default,
                                    CommandType? commandType = null) =>
        new(sql, parameters, Transaction, CommandTimeoutSeconds, commandType, cancellationToken: ct);

    public virtual async Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_table} WHERE Id = @Id" + (_softDelete ? " AND IsDeleted = 0" : "");
        await EnsureOpenAsync(ct);
        return await Connection.QuerySingleOrDefaultAsync<T>(Cmd(sql, new { Id = id }, ct));
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {_table}" + (_softDelete ? " WHERE IsDeleted = 0" : "") + " ORDER BY Id";
        await EnsureOpenAsync(ct);
        return (await Connection.QueryAsync<T>(Cmd(sql, null, ct))).ToList();
    }

    public virtual Task<long> AddAsync(T entity, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteScalarAsync<long>(Cmd(InsertSql, entity, ct));
    });

    public virtual Task<bool> DeleteAsync(long id, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        var sql = _softDelete
            ? $"UPDATE {_table} SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND IsDeleted = 0"
            : $"DELETE FROM {_table} WHERE Id = @Id";
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteAsync(Cmd(sql, new { Id = id }, ct)) > 0;
    });
}
