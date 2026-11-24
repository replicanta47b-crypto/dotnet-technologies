using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

[ApiController]
[Route("api/aggregator")]
public class AggregatorController(PaymentCardService cards, ILogger<AggregatorController> logger) : ControllerBase
{
    /// <summary>
    /// Повна картка платежу: платіж + рахунки з Білінгу + спори. Часткова відповідь із Warnings, якщо сервіс недоступний.
    /// transport=http|grpc — спосіб виклику сервісів (за замовчуванням Aggregator:DefaultTransport = grpc);
    /// nocache=true — оминути кеш Redis (для чесного порівняння транспортів). Заголовки відповіді: X-Transport, X-Cache, X-Elapsed-Ms.
    /// </summary>
    [HttpGet("payments/{id:long}/full")]
    [ProducesResponseType<FullPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FullPaymentDto>> GetFullPayment(
        long id, [FromQuery] string? transport, [FromQuery] bool nocache, CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await cards.GetFullPaymentAsync(id, transport, nocache, ct);
            Response.Headers["X-Transport"] = result.Transport;
            Response.Headers["X-Cache"] = result.Cache;
            Response.Headers["X-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();
            var card = result.Card;
            return card is null
                ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Payment not found",
                    detail: $"Payment {id} was not found.")
                : Ok(card);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // недоступний саме payments-api: без платежу агрегувати нічого
            logger.LogError(ex, "payments-api is unavailable, cannot build the card of payment {PaymentId}", id);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "payments-api is unavailable",
                detail: "The payment cannot be loaded, so there is nothing to aggregate.");
        }
    }
}
