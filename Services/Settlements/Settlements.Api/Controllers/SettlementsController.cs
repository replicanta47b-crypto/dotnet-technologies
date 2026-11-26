using Microsoft.AspNetCore.Mvc;
using Platform.Security;

namespace Settlements.Api.Controllers;

/// <summary>
/// Таблиця 10 завдання (Payments Service → у нас Settlements): усі endpoints лише для admin.
/// Для manager, user і guest Keycloak взагалі не видає permissions settlements:* — сервіс відповідає 403.
/// </summary>
[ApiController]
[Route("api/settlements")]
public class SettlementsController(SettlementStore store, ILogger<SettlementsController> logger) : ControllerBase
{
    /// <summary>Список виплат.</summary>
    [HttpGet]
    [RequirePermission(PlatformPermissions.Settlements.Read)]
    public IActionResult List() => Ok(store.All());

    /// <summary>Провести виплату мерчанту (заглушка).</summary>
    [HttpPost("process")]
    [RequirePermission(PlatformPermissions.Settlements.Process)]
    public IActionResult Process(ProcessSettlementRequest request)
    {
        if (request.Amount <= 0) return Problem("Amount must be positive.", statusCode: StatusCodes.Status400BadRequest);

        var settlement = store.Add(new Settlement(Guid.NewGuid(), request.MerchantId, request.Amount, request.Currency, "Processed", DateTime.UtcNow));
        logger.LogInformation("Settlement {SettlementId} processed for merchant {MerchantId}: {Amount} {Currency} (by {UserId})",
            settlement.Id, settlement.MerchantId, settlement.Amount, settlement.Currency, User.FindFirst("sub")?.Value);
        return Ok(settlement);
    }

    /// <summary>Повернути кошти за виплатою (заглушка).</summary>
    [HttpPost("refund")]
    [RequirePermission(PlatformPermissions.Settlements.Refund)]
    public IActionResult Refund(RefundRequest request)
    {
        var settlement = store.Find(request.SettlementId);
        if (settlement is null) return Problem("Settlement not found.", statusCode: StatusCodes.Status404NotFound);
        if (request.Amount <= 0 || request.Amount > settlement.Amount)
            return Problem("Refund amount must be between 0 and the settlement amount.", statusCode: StatusCodes.Status400BadRequest);

        var refunded = store.Add(settlement with { Status = "Refunded", Reason = request.Reason });
        logger.LogInformation("Settlement {SettlementId} refunded: {Amount}", refunded.Id, request.Amount);
        return Ok(refunded);
    }
}
