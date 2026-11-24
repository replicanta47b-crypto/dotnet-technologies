using Microsoft.Data.SqlClient;
using Payments.Domain.Exceptions;

namespace Payments.Dal.Repositories;

/// <summary>
/// Перетворює помилки SQL Server на доменні винятки (B.5), щоб сирі SQL-повідомлення не потрапляли в API.
/// Номери 50001–50014 — це THROW зі збережених процедур Лаб. 01 (db/p1/procedures.sql).
/// </summary>
public static class SqlExceptionTranslator
{
    public static Exception Translate(SqlException ex) => ex.Number switch
    {
        // 50001 Payment not found, 50006 Customer not found, 50008 Invoice not found
        50001 or 50006 or 50008 => new NotFoundException(ex.Message, ex),

        // 50002 Invalid status transition, 50003 Concurrency conflict, 50004/50005/50007/50009..50014 — бізнес-конфлікти
        >= 50002 and <= 50014 => new BusinessConflictException(ex.Message, ex),

        // порушення UNIQUE
        2601 or 2627 => new BusinessConflictException("Record with the same key already exists", ex),

        // порушення FOREIGN KEY / CHECK
        547 => new BusinessConflictException("Operation violates data integrity constraints", ex),

        // невідома помилка — залишаємо як є (API поверне 500 без технічних деталей)
        _ => ex
    };
}
