using Payments.Bll.Dto;

namespace Payments.Bll.Services;

public interface ICustomerService
{
    Task<CustomerDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerDto>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerDto dto, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto dto, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IInvoiceService
{
    Task<InvoiceDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceDto>> ListAsync(CancellationToken ct = default);
    Task<InvoiceDto> CreateAsync(CreateInvoiceDto dto, CancellationToken ct = default);
    Task<InvoiceDto> UpdateAsync(long id, UpdateInvoiceDto dto, CancellationToken ct = default);
}

public interface IPaymentService
{
    Task<PaymentDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentDto>> ListByCustomerAsync(long customerId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Платіж + деталі + розподіл на рахунки в одній транзакції.</summary>
    Task<PaymentDto> CreateAsync(CreatePaymentDto dto, CancellationToken ct = default);

    Task<PaymentDto> AddAllocationAsync(long paymentId, AllocationRequestDto dto, CancellationToken ct = default);
    Task<PaymentDto> ChangeStatusAsync(long paymentId, ChangePaymentStatusDto dto, CancellationToken ct = default);
    Task<PaymentDto> AuthorizeAsync(long paymentId, AuthorizePaymentDto dto, CancellationToken ct = default);
    Task DeleteAsync(long paymentId, CancellationToken ct = default);
}
