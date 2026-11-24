using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Payments.Dal.Interfaces;
using Payments.Domain.Models;

namespace Payments.Dal.Repositories;

/// <summary>
/// Репозиторій платежів (ADO.NET + Dapper). Читання — multi-mapping, зміни — збережені процедури Лаб. 01.
/// Помилки процедур (THROW 50001–50014) перетворюються на доменні винятки.
/// </summary>
public sealed class PaymentRepository : BaseDapperRepository<Payment>, IPaymentRepository
{
    private const string PaymentColumns =
        "Id, CustomerId, Currency, Status, InitiatedAt, TotalAmount, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    public PaymentRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.Payments", softDelete: true) { }

    public async Task<Payment?> GetWithAllocationsAsync(long paymentId, CancellationToken ct = default)
    {
        // один запит: Payment + PaymentDetails + PaymentAllocations (LEFT JOIN)
        const string sql = @"
            SELECT p.Id, p.CustomerId, p.Currency, p.Status, p.InitiatedAt, p.TotalAmount,
                   p.CreatedAt, p.CreatedBy, p.UpdatedAt, p.UpdatedBy, p.IsDeleted, p.RowVersion,
                   d.Method, d.PayerAccount, d.Description,
                   a.InvoiceId, a.InvoiceNumber, a.InvoiceAmount, a.AllocatedAmount
            FROM dbo.Payments p
            LEFT JOIN dbo.PaymentDetails d ON d.PaymentId = p.Id
            LEFT JOIN dbo.PaymentAllocations a ON a.PaymentId = p.Id
            WHERE p.Id = @PaymentId AND p.IsDeleted = 0
            ORDER BY a.InvoiceId";

        await EnsureOpenAsync(ct);
        Payment? result = null;
        await Connection.QueryAsync<Payment, PaymentDetails?, PaymentAllocation?, Payment>(
            Cmd(sql, new { PaymentId = paymentId }, ct),
            (payment, details, allocation) =>      // кожен рядок JOIN: Payment + деталі + одна позиція розподілу
            {
                result ??= payment;
                result.Details ??= details;
                if (allocation is not null) result.Allocations.Add(allocation);
                return result;
            },
            splitOn: "Method,InvoiceId");          // з цих стовпців починаються PaymentDetails і PaymentAllocation
        return result;
    }

    public async Task<IReadOnlyList<Payment>> GetByCustomerAsync(long customerId, int offset, int take,
                                                                 CancellationToken ct = default)
    {
        // використовує фільтрований індекс IX_Payments_Active (CustomerId, InitiatedAt DESC) WHERE IsDeleted = 0
        const string sql = $@"SELECT {PaymentColumns} FROM dbo.Payments
                              WHERE CustomerId = @CustomerId AND IsDeleted = 0
                              ORDER BY InitiatedAt DESC, Id DESC
                              OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY";
        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<Payment>(
            Cmd(sql, new { CustomerId = customerId, Offset = offset, Take = take }, ct));
        return rows.ToList();
    }

    // generic AddAsync: створення платежу разом з деталями через usp_CreatePayment
    public override async Task<long> AddAsync(Payment entity, CancellationToken ct = default)
    {
        if (entity.Details is null)
            throw new ArgumentException("Payment details are required.", nameof(entity));

        return await TranslateAsync(async () =>
        {
            await EnsureOpenAsync(ct);
            var p = new DynamicParameters();
            p.Add("@CustomerId", entity.CustomerId);
            p.Add("@Currency", entity.Currency);
            p.Add("@Method", entity.Details.Method);
            p.Add("@PayerAccount", entity.Details.PayerAccount);
            p.Add("@Description", entity.Details.Description);
            p.Add("@CreatedBy", entity.CreatedBy);
            p.Add("@NewPaymentId", dbType: DbType.Int64, direction: ParameterDirection.Output);
            await Connection.ExecuteAsync(Cmd("dbo.usp_CreatePayment", p, ct, CommandType.StoredProcedure));
            return p.Get<long>("@NewPaymentId");
        });
    }

    // generic DeleteAsync: м'яке видалення через usp_SoftDeletePayment (дозволено лише для Created/Cancelled)
    public override async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        return await TranslateAsync(async () =>
        {
            await EnsureOpenAsync(ct);
            await Connection.ExecuteAsync(Cmd("dbo.usp_SoftDeletePayment",
                new { PaymentId = id, DeletedBy = "api" }, ct, CommandType.StoredProcedure));
            return true;
        });
    }

    public Task AddAllocationAsync(long paymentId, long invoiceId, decimal? amount, string? updatedBy,
                                   CancellationToken ct = default) => TranslateAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        await Connection.ExecuteAsync(Cmd("dbo.usp_AddPaymentAllocation",
            new { PaymentId = paymentId, InvoiceId = invoiceId, Amount = amount, UpdatedBy = updatedBy },
            ct, CommandType.StoredProcedure));
        return true;
    });

    public Task ChangeStatusAsync(long paymentId, string newStatus, string? changedBy, string? note,
                                  byte[]? expectedRowVersion, CancellationToken ct = default) => TranslateAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        await Connection.ExecuteAsync(Cmd("dbo.usp_ChangePaymentStatus",
            new
            {
                PaymentId = paymentId,
                NewStatus = newStatus,
                ChangedBy = changedBy,
                Note = note,
                ExpectedRowVersion = expectedRowVersion
            },
            ct, CommandType.StoredProcedure));
        return true;
    });

    public Task AuthorizeAsync(long paymentId, decimal amount, string? providerRef, string? authorizedBy,
                               CancellationToken ct = default) => TranslateAsync(async () =>
    {
        await EnsureOpenAsync(ct);
        await Connection.ExecuteAsync(Cmd("dbo.usp_AuthorizePayment",
            new { PaymentId = paymentId, Amount = amount, ProviderRef = providerRef, AuthorizedBy = authorizedBy },
            ct, CommandType.StoredProcedure));
        return true;
    });
}
