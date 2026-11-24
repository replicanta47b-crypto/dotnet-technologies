using AutoMapper;
using Payments.Bll.Dto;
using Payments.Domain.Models;

namespace Payments.Bll.Mapping;

/// <summary>Усі відображення Domain ↔ DTO в одному профілі; реєструється централізовано в AddPaymentsServices.</summary>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Domain → DTO
        CreateMap<Customer, CustomerDto>();
        CreateMap<Invoice, InvoiceDto>();
        CreateMap<PaymentDetails, PaymentDetailsDto>();
        CreateMap<PaymentAllocation, PaymentAllocationDto>();
        CreateMap<Payment, PaymentDto>()
            .ForMember(d => d.RowVersion,
                o => o.MapFrom(s => s.RowVersion == null ? null : Convert.ToBase64String(s.RowVersion)));

        // DTO → Domain (службові поля заповнює сервіс або база)
        CreateMap<CreateCustomerDto, Customer>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore());
        CreateMap<CreateInvoiceDto, Invoice>();
        CreateMap<UpdateInvoiceDto, Invoice>()
            .ForMember(d => d.Id, o => o.Ignore());
        CreateMap<CreatePaymentDto, Payment>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore())
            .ForMember(d => d.Allocations, o => o.Ignore())
            .ForMember(d => d.Details, o => o.MapFrom(s => new PaymentDetails
            {
                Method = s.Method,
                PayerAccount = s.PayerAccount,
                Description = s.Description
            }));
    }
}
