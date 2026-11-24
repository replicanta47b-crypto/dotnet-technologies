using System.Globalization;
using Billing.Bll.Dto;
using Billing.Bll.Services;
using Billing.Domain.Exceptions;
using Grpc.Core;
using Platform.Contracts.Billing;

namespace Billing.Api.Rpc;

/// <summary>
/// gRPC-фасад над тим самим IInvoiceService, що й REST-контролер. Отже gRPC-виклики теж проходять через
/// дворівневий кеш (L1 → L2 → БД). Логіка тут відсутня: лише валідація аргументів, мапінг DTO ↔ proto і помилок ↔ StatusCode.
/// </summary>
public sealed class BillingGrpcService(IInvoiceService invoices, ILogger<BillingGrpcService> logger)
    : BillingService.BillingServiceBase
{
    public override async Task<InvoiceReply> GetInvoice(GetInvoiceRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be a positive number"));

        try
        {
            return ToReply(await invoices.GetByIdAsync(request.Id, context.CancellationToken));
        }
        catch (EntityNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Invoice {request.Id} not found"));
        }
    }

    public override async Task<GetInvoicesReply> GetInvoices(GetInvoicesRequest request, ServerCallContext context)
    {
        if (request.Ids.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ids must not be empty"));
        if (request.Ids.Count > 100)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "at most 100 ids per call"));

        var reply = new GetInvoicesReply();
        foreach (var id in request.Ids.Distinct())
        {
            try
            {
                reply.Invoices.Add(ToReply(await invoices.GetByIdAsync(id, context.CancellationToken)));
            }
            catch (EntityNotFoundException)
            {
                reply.MissingIds.Add(id);
            }
        }
        logger.LogInformation("GetInvoices: requested {Requested}, found {Found}, missing {Missing}",
            request.Ids.Count, reply.Invoices.Count, reply.MissingIds.Count);
        return reply;
    }

    private static InvoiceReply ToReply(InvoiceDto d) => new()
    {
        Id = d.Id,
        Number = d.Number,
        MerchantId = d.MerchantId,
        MerchantName = d.MerchantName ?? "",
        Currency = d.Currency,
        TotalAmount = d.TotalAmount.ToString(CultureInfo.InvariantCulture),
        DueDate = d.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Status = d.Status
    };
}
