using AutoMapper;
using Billing.Bll.Common;
using Billing.Bll.Dto;
using Billing.Dal.Interfaces;
using Billing.Dal.Specifications;
using Billing.Domain.Entities;
using Billing.Domain.Enums;
using Billing.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Billing.Bll.Services;

/// <summary>
/// Бізнес-логіка рахунків. З EF Core напряму не працює — лише через репозиторії та Unit of Work.
/// Правила самої сутності (сума рядка, дозволені переходи статусів) перевіряє Invoice;
/// сервіс — правила, що потребують даних з БД (існування мерчанта/категорії, унікальність номера).
/// SaveChangesAsync викликається один раз наприкінці кожної операції.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private const string Actor = "api";
    private const int MaxPageSize = 100;

    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(IUnitOfWork uow, IMapper mapper, ILogger<InvoiceService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<InvoiceDetailsDto> GetByIdAsync(long id, CancellationToken ct = default)
        => _mapper.Map<InvoiceDetailsDto>(await RequireAsync(id, ct));

    public async Task<PagedResult<InvoiceDto>> GetListAsync(InvoiceQuery query, CancellationToken ct = default)
    {
        if (query.Page < 1)
            throw new ValidationException("Page must be 1 or greater.");

        var sortBy = (query.SortBy ?? "dueDate").ToLowerInvariant();
        if (!InvoicesPageSpec.SortFields.Contains(sortBy))
            throw new ValidationException($"SortBy must be one of: {string.Join(", ", InvoicesPageSpec.SortFields)}.");

        var sortDir = (query.SortDir ?? "asc").ToLowerInvariant();
        if (sortDir is not ("asc" or "desc"))
            throw new ValidationException("SortDir must be 'asc' or 'desc'.");

        InvoiceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<InvoiceStatus>(query.Status, ignoreCase: true, out var parsed))
                throw new ValidationException($"Status must be one of: {string.Join(", ", Enum.GetNames<InvoiceStatus>())}.");
            status = parsed;
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var criteria = new InvoiceListCriteria(query.Page, pageSize, query.Search, status, query.MerchantId,
                                               query.MinAmount, query.MaxAmount, sortBy, sortDir);

        // фільтрація, сортування й Skip/Take виконуються в SQL; totalCount рахує БД
        var total = await _uow.Invoices.CountAsync(new InvoicesFilterSpec(criteria), ct);
        var items = await _uow.Invoices.ListAsync(new InvoicesPageSpec(criteria), ct);

        return new PagedResult<InvoiceDto>(_mapper.Map<List<InvoiceDto>>(items), total, query.Page, pageSize);
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetOverdueAsync(CancellationToken ct = default)
    {
        // друга специфікація — той самий ListAsync без змін у репозиторії
        var spec = new OverdueInvoicesSpec(DateOnly.FromDateTime(DateTime.UtcNow));
        return _mapper.Map<List<InvoiceDto>>(await _uow.Invoices.ListAsync(spec, ct));
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetByCategoryAsync(string categorySlug, DateTime? since, CancellationToken ct = default)
    {
        var from = since ?? DateTime.UtcNow.AddDays(-30);
        var invoices = await _uow.InvoiceCategories.GetRecentlyAssignedAsync(categorySlug.ToLowerInvariant(), from, ct);
        return _mapper.Map<List<InvoiceDto>>(invoices);
    }

    public async Task<InvoiceDetailsDto> CreateAsync(CreateInvoiceDto input, CancellationToken ct = default)
    {
        using var activity = Telemetry.Source.StartActivity("CreateInvoice");
        activity?.SetTag("merchant.id", input.MerchantId);

        var merchant = await _uow.Merchants.GetByIdAsync(input.MerchantId, ct)
                       ?? throw new EntityNotFoundException(nameof(Merchant), input.MerchantId);
        if (!merchant.IsActive)
            throw new ConflictException($"Merchant '{merchant.Name}' is not active.");

        var number = input.Number.Trim();
        if (await _uow.Invoices.ExistsByNumberAsync(number, ct))
            throw new ConflictException($"Invoice with number '{number}' already exists.");

        var invoice = new Invoice(number, input.MerchantId, input.Currency, input.DueDate, Actor);
        foreach (var line in input.Lines)
            invoice.AddLine(line.Description, line.Quantity, line.UnitPrice);
        invoice.SetDetail(input.CustomerRef, input.PeriodStart, input.PeriodEnd, input.Note);
        invoice.Issue();

        await _uow.Invoices.AddAsync(invoice, ct);
        await _uow.SaveChangesAsync(ct);
        activity?.SetTag("invoice.id", invoice.Id);
        _logger.LogInformation(
            "Invoice {InvoiceId} {InvoiceNumber} created for merchant {MerchantId}: {Currency}, {LinesCount} line(s), due {DueDate}",
            invoice.Id, invoice.Number, invoice.MerchantId, invoice.Currency, input.Lines.Count, input.DueDate);

        return await GetByIdAsync(invoice.Id, ct);
    }

    public Task<InvoiceDetailsDto> MarkPaidAsync(long id, CancellationToken ct = default)
        => MutateAsync(id, invoice => invoice.MarkPaid(), ct);

    public Task<InvoiceDetailsDto> VoidAsync(long id, CancellationToken ct = default)
        => MutateAsync(id, invoice => invoice.Void(), ct);

    public Task<InvoiceDetailsDto> RescheduleAsync(long id, RescheduleInvoiceDto input, CancellationToken ct = default)
        => MutateAsync(id, invoice => invoice.Reschedule(input.DueDate), ct);

    public async Task<InvoiceDetailsDto> AssignCategoryAsync(long id, AssignCategoryDto input, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(id, ct);
        _ = await _uow.Categories.GetByIdAsync(input.CategoryId, ct)
            ?? throw new EntityNotFoundException(nameof(ServiceCategory), input.CategoryId);

        invoice.AssignCategory(input.CategoryId, input.IsPrimary);
        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(id, ct);
        invoice.MarkDeleted();                       // м'яке видалення
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Invoice {InvoiceId} deleted", id);
    }

    // ---- допоміжні ----

    private async Task<Invoice> RequireAsync(long id, CancellationToken ct)
        => await _uow.Invoices.GetWithDetailsAsync(id, ct)
           ?? throw new EntityNotFoundException(nameof(Invoice), id);

    private async Task<InvoiceDetailsDto> MutateAsync(long id, Action<Invoice> change, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("ChangeInvoiceStatus");
        activity?.SetTag("invoice.id", id);

        var invoice = await RequireAsync(id, ct);
        change(invoice);                             // правила переходів — всередині сутності (DomainException → 409)
        await _uow.SaveChangesAsync(ct);             // один SaveChanges — одна транзакція
        _logger.LogInformation("Invoice {InvoiceId} changed, status is now {Status}", id, invoice.Status);
        return _mapper.Map<InvoiceDetailsDto>(invoice);
    }
}
