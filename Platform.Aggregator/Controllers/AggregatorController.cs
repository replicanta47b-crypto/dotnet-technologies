using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

[ApiController]
[Route("api/aggregator")]
public class AggregatorController(PaymentCardService cards, ILogger<AggregatorController> logger) : ControllerBase
{
    /// <summary>Повна картка платежу: платіж + рахунки з Білінгу + спори. Часткова відповідь із Warnings, якщо сервіс недоступний.</summary>
    [HttpGet("payments/{id:long}/full")]
    [ProducesResponseType<FullPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FullPaymentDto>> GetFullPayment(long id, CancellationToken ct)
    {
        try
        {
            var card = await cards.GetFullPaymentAsync(id, ct);
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
