using AutoMapper;
using Microsoft.Extensions.Logging;
using Payments.Bll.Common;
using Payments.Bll.Dto;
using Payments.Dal.Interfaces;
using Payments.Domain.Constants;
using Payments.Domain.Exceptions;
using Payments.Domain.Models;

namespace Payments.Bll.Services;

public sealed class InvoiceService : IInvoiceService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(IUnitOfWork uow, IMapper mapper, ILogger<InvoiceService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<InvoiceDto> GetAsync(long id, CancellationToken ct = default)
    {
        var invoice = await _uow.Invoices.GetByIdAsync(id, ct)
                      ?? throw new NotFoundException($"Invoice {id} was not found.");
        return _mapper.Map<InvoiceDto>(invoice);
    }

    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(CancellationToken ct = default) =>
        _mapper.Map<List<InvoiceDto>>(await _uow.Invoices.GetAllAsync(ct));

    public Task<InvoiceDto> CreateAsync(CreateInvoiceDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            ValidateCurrency(dto.Currency);

            if (await _uow.Invoices.GetByIdAsync(dto.Id, ct) is not null)
                throw new BusinessConflictException($"Invoice {dto.Id} already exists.");
            if (await _uow.Invoices.GetByNumberAsync(dto.Number.Trim(), ct) is not null)
                throw new BusinessConflictException($"Invoice number '{dto.Number}' already exists.");

            var invoice = _mapper.Map<Invoice>(dto);
            invoice.Number = dto.Number.Trim();
            invoice.Currency = dto.Currency.ToUpperInvariant();
            await _uow.Invoices.AddAsync(invoice, ct);
            _logger.LogInformation("Invoice {InvoiceId} synced", invoice.Id);

            return _mapper.Map<InvoiceDto>(await _uow.Invoices.GetByIdAsync(invoice.Id, ct));
        }, _logger, ct: ct);

    public Task<InvoiceDto> UpdateAsync(long id, UpdateInvoiceDto dto, CancellationToken ct = default) =>
        _uow.InTransactionAsync(async () =>
        {
            ValidateCurrency(dto.Currency);

            _ = await _uow.Invoices.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"Invoice {id} was not found.");

            var sameNumber = await _uow.Invoices.GetByNumberAsync(dto.Number.Trim(), ct);
            if (sameNumber is not null && sameNumber.Id != id)
                throw new BusinessConflictException($"Invoice number '{dto.Number}' already exists.");

            var invoice = _mapper.Map<Invoice>(dto);
            invoice.Id = id;
            invoice.Number = dto.Number.Trim();
            invoice.Currency = dto.Currency.ToUpperInvariant();
            await _uow.Invoices.UpdateAsync(invoice, ct);

            return _mapper.Map<InvoiceDto>(await _uow.Invoices.GetByIdAsync(id, ct));
        }, _logger, ct: ct);

    private static void ValidateCurrency(string currency)
    {
        if (!Currencies.All.Contains(currency.ToUpperInvariant()))
            throw new ValidationException($"Currency must be one of: {string.Join(", ", Currencies.All)}.");
    }
}
