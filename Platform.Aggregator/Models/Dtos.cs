namespace Platform.Aggregator.Models;

// ---- DTO відповідей окремих сервісів: лише потрібні агрегатору поля (решту ігноруємо) ----

public record PaymentAllocationDto(long InvoiceId, string InvoiceNumber, decimal InvoiceAmount, decimal AllocatedAmount);

public record PaymentDto(
    long Id,
    long CustomerId,
    string Currency,
    string Status,
    DateTime InitiatedAt,
    decimal TotalAmount,
    List<PaymentAllocationDto> Allocations);

public record BillingInvoiceDto(
    long Id,
    string Number,
    long MerchantId,
    string? MerchantName,
    string Currency,
    decimal TotalAmount,
    DateOnly DueDate,
    string Status);

public record DisputeDto(
    string Id,
    long PaymentId,
    long InvoiceId,
    long MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency,
    string Reason,
    string Status,
    DateTime OpenedAt,
    int CommentsCount);

public record PagedDisputes(IReadOnlyList<DisputeDto> Items, long TotalCount);

// ---- композитна відповідь під бізнес-сценарій «картка платежу» ----

/// <summary>Платіж, рахунки з Білінгу, на які він розподілений, і спори за цим платежем + перелік попереджень.</summary>
public record FullPaymentDto(
    PaymentDto Payment,
    IReadOnlyList<BillingInvoiceDto> Invoices,
    IReadOnlyList<DisputeDto> Disputes,
    IReadOnlyList<string> Warnings);
