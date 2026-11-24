using System.Data;
using Microsoft.Extensions.Logging;
using Payments.Dal.Interfaces;

namespace Payments.Bll.Common;

public static class UnitOfWorkExtensions
{
    /// <summary>
    /// Виконує бізнес-операцію в одній транзакції: Begin → дія → Commit; при будь-якій помилці — Rollback
    /// і повторний викид винятку (його перетворить на ProblemDetails центральний обробник).
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(this IUnitOfWork uow, Func<Task<T>> action,
        ILogger logger, IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(level, ct);
        try
        {
            var result = await action();
            await uow.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Transaction rolled back: {Message}", ex.Message);
            await uow.RollbackAsync(CancellationToken.None);   // відкат не повинен скасовуватись разом із запитом
            throw;
        }
    }
}
