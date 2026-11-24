using AutoMapper;
using Billing.Bll.Dto;
using Billing.Dal.Interfaces;
using Billing.Domain.Entities;
using Billing.Domain.Exceptions;

namespace Billing.Bll.Services;

public class MerchantService : IMerchantService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public MerchantService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
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
}
