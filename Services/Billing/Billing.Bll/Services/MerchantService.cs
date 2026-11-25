using AutoMapper;
using Billing.Bll.Dto;
using Billing.Bll.Events;
using Billing.Dal.Interfaces;
using Billing.Domain.Entities;
using Billing.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Billing.Bll.Services;

public class MerchantService : IMerchantService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    private readonly IBillingEventPublisher _events;
    private readonly ILogger<MerchantService> _logger;

    public MerchantService(IUnitOfWork uow, IMapper mapper, IBillingEventPublisher events, ILogger<MerchantService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _events = events;
        _logger = logger;
    }

    /// <summary>Мерчант + його рахунки (Explicit Loading: два окремих запити).</summary>
    public async Task<MerchantDetailsDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var merchant = await _uow.Merchants.GetWithInvoicesAsync(id, ct)
                       ?? throw new EntityNotFoundException(nameof(Merchant), id);
        return _mapper.Map<MerchantDetailsDto>(merchant);
    }

    public async Task<IReadOnlyList<MerchantDto>> GetAllAsync(CancellationToken ct = default)
        => _mapper.Map<List<MerchantDto>>(await _uow.Merchants.GetAllAsync(ct));

    public async Task<MerchantDto> CreateAsync(CreateMerchantDto input, CancellationToken ct = default)
    {
        var name = input.Name.Trim();
        if (await _uow.Merchants.ExistsByNameAsync(name, ct))
            throw new ConflictException($"Merchant '{name}' already exists.");

        var merchant = new Merchant(name, input.Country, input.ContactEmail);
        await _uow.Merchants.AddAsync(merchant, ct);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<MerchantDto>(merchant);
    }

    /// <summary>Перейменування мерчанта → подія MerchantRenamedEvent (копії назви в Payments і Disputes оновлюються асинхронно).</summary>
    public async Task<MerchantDto> RenameAsync(long id, RenameMerchantDto input, CancellationToken ct = default)
    {
        var merchant = await _uow.Merchants.GetByIdAsync(id, ct)
                       ?? throw new EntityNotFoundException(nameof(Merchant), id);

        var newName = input.Name.Trim();
        var oldName = merchant.Name;
        if (!string.Equals(oldName, newName, StringComparison.Ordinal)
            && await _uow.Merchants.ExistsByNameAsync(newName, ct))
            throw new ConflictException($"Merchant '{newName}' already exists.");

        if (!merchant.Rename(newName))
            return _mapper.Map<MerchantDto>(merchant);        // назва та сама — нічого не змінилось, подія не потрібна

        await _uow.SaveChangesAsync(ct);                       // 1) зміни збережено
        _logger.LogInformation("Merchant {MerchantId} renamed: {OldName} -> {NewName}", id, oldName, newName);

        await _events.MerchantRenamedAsync(id, oldName, newName, ct);   // 2) лише тоді — подія
        return _mapper.Map<MerchantDto>(merchant);
    }
}
