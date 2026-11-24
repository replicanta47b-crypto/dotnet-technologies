using System.Data;
using Microsoft.Data.SqlClient;

namespace Payments.Dal.Repositories;

/// <summary>
/// Спільна основа репозиторіїв: одне з'єднання та доступ до ПОТОЧНОЇ транзакції Unit of Work.
/// Репозиторій отримує не рядок підключення, а з'єднання й делегат транзакції.
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>Таймаут команд, секунд. Довші запити краще переривати, ніж тримати блокування.</summary>
    protected const int CommandTimeoutSeconds = 30;

    protected readonly SqlConnection Connection;
    private readonly Func<SqlTransaction?> _currentTransaction;

    protected RepositoryBase(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    {
        Connection = connection;
        _currentTransaction = currentTransaction;
    }

    /// <summary>Поточна транзакція Unit of Work (або null, якщо її не розпочато).</summary>
    protected SqlTransaction? Transaction => _currentTransaction();

    protected async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
            await Connection.OpenAsync(ct);
    }

    /// <summary>Виконує дію і перетворює SqlException на доменні винятки (B.5).</summary>
    protected static async Task<T> TranslateAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.Translate(ex);
        }
    }
}
