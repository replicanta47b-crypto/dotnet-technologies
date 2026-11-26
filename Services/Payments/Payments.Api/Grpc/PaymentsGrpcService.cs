using System.Globalization;
using Grpc.Core;
using Platform.Security;
using Payments.Bll.Services;
using Payments.Domain.Exceptions;
using Platform.Contracts.Payments;

namespace Payments.Api.Rpc;

/// <summary>gRPC-фасад над IPaymentService (той самий BLL, що й у REST). Помилки домену мапляться на StatusCode.</summary>
[RequirePermission(PlatformPermissions.Payments.Read)]   // Лаб. 09: Aggregator викликає зі scope payments.read (Client Credentials)
public sealed class PaymentsGrpcService(IPaymentService payments) : PaymentsService.PaymentsServiceBase
{
    public override async Task<PaymentReply> GetPayment(GetPaymentRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be a positive number"));

        try
        {
            var p = await payments.GetAsync(request.Id, context.CancellationToken);
            var reply = new PaymentReply
            {
                Id = p.Id,
                CustomerId = p.CustomerId,
                Currency = p.Currency,
                Status = p.Status,
                InitiatedAt = p.InitiatedAt.ToString("O", CultureInfo.InvariantCulture),
                TotalAmount = p.TotalAmount.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var a in p.Allocations)
                reply.Allocations.Add(new AllocationReply
                {
                    InvoiceId = a.InvoiceId,
                    InvoiceNumber = a.InvoiceNumber ?? "",
                    InvoiceAmount = a.InvoiceAmount.ToString(CultureInfo.InvariantCulture),
                    AllocatedAmount = a.AllocatedAmount.ToString(CultureInfo.InvariantCulture)
                });
            return reply;
        }
        catch (NotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Payment {request.Id} not found"));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }
}
