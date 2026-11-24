using AutoMapper;
using Microsoft.Extensions.Logging;
using Payments.Bll.Common;
using Payments.Bll.Dto;
using Payments.Dal.Interfaces;
using Payments.Domain.Constants;
using Payments.Domain.Exceptions;
using Payments.Domain.Models;

namespace Payments.Bll.Services;

public sealed class PaymentService : IPaymentService
{
    private const string Actor = "api";

    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IUnitOfWork uow, IMapper mapper, ILogger<PaymentService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public Task<PaymentDto> GetAsync(long id, CancellationToken ct = default) => LoadAsync(id, ct);

    public async Task<IReadOnlyList<PaymentDto>> ListByCustomerAsync(long customerId, int page, int pageSize,
                                                                     CancellationToken ct = default)
    {
        Pagination.Validate(page, pageSize);
        _ = await _uow.Customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException($"Customer {customerId} was not found.");
        var rows = await _uow.Payments.GetByCustomerAsync(customerId, (page - 1) * pageSize, pageSize, ct);
        return _mapper.Map<List<PaymentDto>>(rows);
    }

    /// <summary>
    /// Транзакційна багатокрокова операція: платіж → деталі → розподіл на рахунки.
    /// Номер і суму рахунку «заморожує» (знімок) процедура usp_AddPaymentAllocation; якщо будь-який крок
    /// падає (нема рахунку, інша валюта, сума більша за рахунок) — відкочується ВЕСЬ платіж.
    /// </summary>
    public Task<PaymentDto> CreateAsync(CreatePaymentDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            using var activity = Telemetry.Source.StartActivity("CreatePayment");
            activity?.SetTag("customer.id", dto.CustomerId);

            // ---- другий рівень валідації: бізнес-правила (перший — DataAnnotations на DTO) ----
            var currency = dto.Currency.Trim().ToUpperInvariant();
            if (!Currencies.All.Contains(currency))
                throw new ValidationException($"Currency must be one of: {string.Join(", ", Currencies.All)}.");
            if (!PaymentMethods.All.Contains(dto.Method))
                throw new ValidationException($"Method must be one of: {string.Join(", ", PaymentMethods.All)}.");
            if (dto.Allocations.Select(a => a.InvoiceId).Distinct().Count() != dto.Allocations.Count)
                throw new ValidationException("The same invoice cannot be listed twice.");

            _ = await _uow.Customers.GetByIdAsync(dto.CustomerId, ct)
                ?? throw new NotFoundException($"Customer {dto.CustomerId} was not found.");

            // усі рахунки мають існувати й бути в валюті платежу — перевіряємо до запису
            foreach (var a in dto.Allocations)
            {
                var invoice = await _uow.Invoices.GetByIdAsync(a.InvoiceId, ct)
                              ?? throw new NotFoundException($"Invoice {a.InvoiceId} was not found.");
                if (!string.Equals(invoice.Currency, currency, StringComparison.OrdinalIgnoreCase))
                    throw new BusinessConflictException(
                        $"Invoice {invoice.Id} is in {invoice.Currency}, payment is in {currency}.");
            }

            // ---- запис ----
            var payment = _mapper.Map<Payment>(dto);
            payment.Currency = currency;
            payment.CreatedBy = Actor;
            var paymentId = await _uow.Payments.AddAsync(payment, ct);

            foreach (var a in dto.Allocations)
                await _uow.Payments.AddAllocationAsync(paymentId, a.InvoiceId, a.Amount, Actor, ct);

            activity?.SetTag("payment.id", paymentId);
            _logger.LogInformation(
                "Payment {PaymentId} created for customer {CustomerId}: {Currency}, {Count} allocation(s), method {Method}",
                paymentId, dto.CustomerId, currency, dto.Allocations.Count, dto.Method);
            return await LoadAsync(paymentId, ct);
        }, _logger, ct: ct);

    public Task<PaymentDto> AddAllocationAsync(long paymentId, AllocationRequestDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            await _uow.Payments.AddAllocationAsync(paymentId, dto.InvoiceId, dto.Amount, Actor, ct);
            return await LoadAsync(paymentId, ct);
        }, _logger, ct: ct);

    public Task<PaymentDto> ChangeStatusAsync(long paymentId, ChangePaymentStatusDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            using var activity = Telemetry.Source.StartActivity("ChangePaymentStatus");
            activity?.SetTag("payment.id", paymentId);
            activity?.SetTag("payment.status", dto.NewStatus);

            if (!PaymentStatuses.All.Contains(dto.NewStatus))
                throw new ValidationException($"Status must be one of: {string.Join(", ", PaymentStatuses.All)}.");

            byte[]? rowVersion = null;
            if (!string.IsNullOrWhiteSpace(dto.RowVersion))
            {
                try { rowVersion = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { throw new ValidationException("RowVersion must be a Base64 string."); }
            }

            // допустимість переходу (Created → Validating → … ) і конкурентність перевіряє usp_ChangePaymentStatus;
            // недопустимий перехід → 50002 → BusinessConflictException → 409
            await _uow.Payments.ChangeStatusAsync(paymentId, dto.NewStatus, Actor, dto.Note, rowVersion, ct);
            _logger.LogInformation("Payment {PaymentId} moved to {Status}", paymentId, dto.NewStatus);
            return await LoadAsync(paymentId, ct);
        }, _logger, ct: ct);

    public Task<PaymentDto> AuthorizeAsync(long paymentId, AuthorizePaymentDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            await _uow.Payments.AuthorizeAsync(paymentId, dto.Amount, dto.ProviderRef, Actor, ct);
            return await LoadAsync(paymentId, ct);
        }, _logger, ct: ct);

    public Task DeleteAsync(long paymentId, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            await _uow.Payments.DeleteAsync(paymentId, ct);
            return true;
        }, _logger, ct: ct);

    private async Task<PaymentDto> LoadAsync(long id, CancellationToken ct)
    {
        var payment = await _uow.Payments.GetWithAllocationsAsync(id, ct)
                      ?? throw new NotFoundException($"Payment {id} was not found.");
        return _mapper.Map<PaymentDto>(payment);
    }
}
