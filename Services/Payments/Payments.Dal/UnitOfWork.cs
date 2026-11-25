using System.Data;
using Microsoft.Data.SqlClient;
using Payments.Dal.Interfaces;
using Payments.Dal.Repositories;

namespace Payments.Dal;

/// <summary>
/// Unit of Work: ОДНЕ з'єднання на всю операцію, транзакція з потрібним рівнем ізоляції,
/// усі репозиторії отримують те саме з'єднання й доступ до ПОТОЧНОЇ транзакції.
///
/// Рівень ізоляції за замовчуванням — ReadCommitted: читання не бачать незафіксованих змін,
/// а блокування тримаються коротко. Критичні зміни статусу додатково захищені UPDLOCK у збережених
/// процедурах (db/p1/procedures.sql) і перевіркою RowVersion. Вищі рівні (RepeatableRead, Serializable)
/// зменшують аномалії читання, але збільшують блокування й ризик deadlock — тому застосовуються лише точково.
/// Таймаут команд — 30 с (див. RepositoryBase.CommandTimeoutSeconds).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    public UnitOfWork(string connectionString)
    {
        _connection = new SqlConnection(connectionString);

        // усі репозиторії отримують ОДНЕ з'єднання і доступ до ПОТОЧНОЇ транзакції
        Customers = new CustomerRepository(_connection, () => _transaction);
        Invoices = new InvoiceRepository(_connection, () => _transaction);
        Payments = new PaymentRepository(_connection, () => _transaction);
        ProcessedEvents = new ProcessedEventRepository(_connection, () => _transaction);
    }

    public ICustomerRepository Customers { get; }
    public IInvoiceRepository Invoices { get; }
    public IPaymentRepository Payments { get; }
    public IProcessedEventRepository ProcessedEvents { get; }

    public async Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted,
                                            CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("A transaction is already in progress.");

        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);

        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(level, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No transaction to commit.");

        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;

        try
        {
            // якщо сервер уже відкотив транзакцію (XACT_ABORT + THROW у процедурі), Connection == null
            if (_transaction.Connection is not null)
                await _transaction.RollbackAsync(ct);
        }
        catch (InvalidOperationException)
        {
            // транзакція вже завершена сервером — відкат не потрібен
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();   // незавершена транзакція відкочується
        await _connection.DisposeAsync();         // з'єднання повертається в пул
    }
}
