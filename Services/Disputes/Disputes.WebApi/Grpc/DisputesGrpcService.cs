using System.Globalization;
using Disputes.Application.Dtos;
using Disputes.Application.Queries.GetDisputesList;
using Grpc.Core;
using MediatR;
using Platform.Contracts.Disputes;

namespace Disputes.WebApi.Rpc;

/// <summary>gRPC-фасад над тим самим MediatR-запитом (CQRS), що використовує й REST-контролер.</summary>
public sealed class DisputesGrpcService(ISender mediator) : DisputesService.DisputesServiceBase
{
    public override async Task<DisputesReply> GetDisputesByPayment(GetDisputesByPaymentRequest request, ServerCallContext context)
    {
        if (request.PaymentId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "payment_id must be a positive number"));

        var pageSize = request.PageSize > 0 ? Math.Min(request.PageSize, 100) : 50;
        var page = await mediator.Send(
            new GetDisputesListQuery(PaymentId: request.PaymentId, Page: 1, PageSize: pageSize),
            context.CancellationToken);

        var reply = new DisputesReply { TotalCount = page.TotalCount };
        foreach (var d in page.Items)
            reply.Items.Add(ToReply(d));
        return reply;
    }

    private static DisputeReply ToReply(DisputeDto d) => new()
    {
        Id = d.Id,
        PaymentId = d.PaymentId,
        InvoiceId = d.InvoiceId,
        MerchantId = d.MerchantId,
        MerchantName = d.MerchantName ?? "",
        Amount = d.Amount.ToString(CultureInfo.InvariantCulture),
        Currency = d.Currency,
        Reason = d.Reason,
        Status = d.Status,
        OpenedAt = d.OpenedAt.ToString("O", CultureInfo.InvariantCulture),
        CommentsCount = d.CommentsCount
    };
}
