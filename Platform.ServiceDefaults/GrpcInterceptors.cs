using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Platform.ServiceDefaults;

/// <summary>
/// Серверний interceptor gRPC (Лаб. 06). Для кожного unary-виклику:
///  • бере CorrelationId з metadata (x-correlation-id) → LogContext, тег span → усі логи виклику мають той самий id, що й REST;
///  • логує початок, кінець і тривалість виклику (boundary logging, як у CorrelationIdMiddleware);
///  • бізнес-помилки сервіси мапують на RpcException самі (NotFound, InvalidArgument…), а будь-який
///    «несподіваний» виняток тут перетворюється на StatusCode.Internal БЕЗ витоку деталей клієнту.
/// </summary>
public sealed class GrpcServerInterceptor(ILogger<GrpcServerInterceptor> logger) : Interceptor
{
    public const string CorrelationMetadataKey = "x-correlation-id";   // ключі metadata gRPC — нижній регістр

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var correlationId = context.RequestHeaders.GetValue(CorrelationMetadataKey);
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            logger.LogInformation("CorrelationId {CorrelationId} generated: gRPC call had no {Key} metadata",
                correlationId, CorrelationMetadataKey);
        }

        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetTag("rpc.grpc.method", context.Method);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Method))
        {
            var sw = Stopwatch.StartNew();
            logger.LogInformation("gRPC call started {Method} from {Peer}", context.Method, context.Peer);
            try
            {
                var response = await continuation(request, context);
                logger.LogInformation("gRPC call finished {Method} from {Peer} with {StatusCode} in {ElapsedMs} ms",
                    context.Method, context.Peer, StatusCode.OK, sw.ElapsedMilliseconds);
                return response;
            }
            catch (RpcException ex)
            {
                logger.LogWarning("gRPC call finished {Method} from {Peer} with {StatusCode} in {ElapsedMs} ms: {Detail}",
                    context.Method, context.Peer, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "gRPC call failed {Method} from {Peer} with {StatusCode} after {ElapsedMs} ms",
                    context.Method, context.Peer, StatusCode.Internal, sw.ElapsedMilliseconds);
                throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
            }
        }
    }
}

/// <summary>Клієнтський interceptor: передає CorrelationId поточного HTTP-запиту у metadata вихідного gRPC-виклику.</summary>
public sealed class GrpcClientInterceptor(IHttpContextAccessor accessor) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var id = accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (string.IsNullOrEmpty(id))
            return continuation(request, context);

        var headers = context.Options.Headers ?? new Metadata();
        headers.Add(GrpcServerInterceptor.CorrelationMetadataKey, id);
        var options = context.Options.WithHeaders(headers);
        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options));
    }
}
