using AutoMapper;
using Billing.Bll.Dto;
using Billing.Domain.Entities;

namespace Billing.Bll.Mapping;

/// <summary>Усі відображення Entity → DTO в одному профілі; реєструється централізовано в Program.cs.</summary>
public class BillingProfile : Profile
{
    public BillingProfile()
    {
        CreateMap<Invoice, InvoiceDto>()
            .ForMember(d => d.MerchantName, o => o.MapFrom(s => s.Merchant == null ? null : s.Merchant.Name));

        CreateMap<InvoiceLine, InvoiceLineDto>();
        CreateMap<InvoiceDetail, InvoiceDetailDto>();
        CreateMap<InvoiceCategory, InvoiceCategoryDto>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Category.Name))
            .ForMember(d => d.Slug, o => o.MapFrom(s => s.Category.Slug));

        CreateMap<Invoice, InvoiceDetailsDto>()
            .ForMember(d => d.MerchantName, o => o.MapFrom(s => s.Merchant == null ? null : s.Merchant.Name))
            .ForMember(d => d.Categories, o => o.MapFrom(s => s.InvoiceCategories));

        CreateMap<Merchant, MerchantDto>();
        CreateMap<Merchant, MerchantDetailsDto>();
    }
}
