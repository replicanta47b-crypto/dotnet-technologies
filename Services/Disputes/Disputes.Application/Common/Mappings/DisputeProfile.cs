using AutoMapper;
using Disputes.Application.Dtos;
using Disputes.Domain.Common;
using Disputes.Domain.Entities;
using Disputes.Domain.ValueObjects;

namespace Disputes.Application.Common.Mappings;

/// <summary>
/// Мапінг сутність → DTO: ObjectId → string, enum → snake_case рядок, Value Objects і вкладені масиви.
/// Конструктор DTO викликається явно (ConstructUsing), щоб не залежати від збігу назв параметрів.
/// </summary>
public class DisputeProfile : Profile
{
    public DisputeProfile()
    {
        CreateMap<Comment, CommentDto>()
            .ConstructUsing(c => new CommentDto(c.AuthorName, c.Text, c.CreatedAt))
            .ForAllMembers(o => o.Ignore());

        CreateMap<EvidenceItem, EvidenceDto>()
            .ConstructUsing(e => new EvidenceDto(e.Type, e.Url, e.AddedAt))
            .ForAllMembers(o => o.Ignore());

        CreateMap<Dispute, DisputeDetailsDto>()
            .ConstructUsing((d, ctx) => new DisputeDetailsDto(
                d.Id.ToString(),
                d.PaymentId,
                d.InvoiceId,
                d.MerchantId,
                d.MerchantName,
                d.Amount,
                d.Currency,
                EnumText.ToText(d.Reason),
                EnumText.ToText(d.Status),
                d.OpenedBy.UserId,
                d.OpenedBy.DisplayName,
                d.OpenedAt,
                d.ResolvedAt,
                d.ThreadId?.ToString(),
                d.Version,
                d.Evidence.Select(e => ctx.Mapper.Map<EvidenceDto>(e)).ToList(),
                d.Comments.Select(c => ctx.Mapper.Map<CommentDto>(c)).ToList()))
            .ForAllMembers(o => o.Ignore());
    }
}
