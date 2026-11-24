using AutoMapper;
using Disputes.Application.Commands.AddComment;
using Disputes.Application.Commands.CreateDispute;
using Disputes.Application.Commands.UpdateDispute;
using Disputes.WebApi.Contracts;

namespace Disputes.WebApi.Mapping;

/// <summary>Запити API → команди MediatR. Id з маршруту й версія з If-Match додаються в контролері.</summary>
public class ApiMappingProfile : Profile
{
    public ApiMappingProfile()
    {
        CreateMap<CreateDisputeRequest, CreateDisputeCommand>()
            .ConstructUsing(r => new CreateDisputeCommand(
                r.PaymentId, r.InvoiceId, r.MerchantId, r.MerchantName,
                r.Amount, r.Currency, r.Reason, r.OpenedByUserId, r.OpenedByName))
            .ForAllMembers(o => o.Ignore());

        CreateMap<UpdateDisputeRequest, UpdateDisputeCommand>()
            .ConstructUsing(r => new UpdateDisputeCommand(string.Empty, r.Status, null))
            .ForAllMembers(o => o.Ignore());

        CreateMap<AddCommentRequest, AddCommentCommand>()
            .ConstructUsing(r => new AddCommentCommand(string.Empty, r.Author, r.Text, null))
            .ForAllMembers(o => o.Ignore());
    }
}
