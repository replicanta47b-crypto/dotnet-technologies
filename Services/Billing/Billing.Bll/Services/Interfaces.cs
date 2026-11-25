using Billing.Bll.Dto;

namespace Billing.Bll.Services;

public interface IInvoiceService
{
    Task<InvoiceDetailsDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<InvoiceDto>> GetListAsync(InvoiceQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceDto>> GetOverdueAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceDto>> GetByCategoryAsync(string categorySlug, DateTime? since, CancellationToken ct = default);

    Task<InvoiceDetailsDto> CreateAsync(CreateInvoiceDto input, CancellationToken ct = default);
    Task<InvoiceDetailsDto> MarkPaidAsync(long id, CancellationToken ct = default);
    Task<InvoiceDetailsDto> VoidAsync(long id, CancellationToken ct = default);
    Task<InvoiceDetailsDto> RescheduleAsync(long id, RescheduleInvoiceDto input, CancellationToken ct = default);
    Task<InvoiceDetailsDto> AddSurchargeAsync(long id, AddSurchargeDto input, CancellationToken ct = default);
    Task<InvoiceDetailsDto> AssignCategoryAsync(long id, AssignCategoryDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IMerchantService
{
    Task<MerchantDetailsDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<MerchantDto>> GetAllAsync(CancellationToken ct = default);
    Task<MerchantDto> CreateAsync(CreateMerchantDto input, CancellationToken ct = default);
    Task<MerchantDto> RenameAsync(long id, RenameMerchantDto input, CancellationToken ct = default);
}
