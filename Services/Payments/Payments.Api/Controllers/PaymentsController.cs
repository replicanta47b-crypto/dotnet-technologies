using Microsoft.AspNetCore.Mvc;
using Payments.Bll.Dto;
using Payments.Bll.Services;

namespace Payments.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _service;

    public PaymentsController(IPaymentService service) => _service = service;

    [HttpGet("{id:long}")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> Get(long id, CancellationToken ct) =>
        Ok(await _service.GetAsync(id, ct));

    /// <summary>Платежі платника (нові — першими).</summary>
    [HttpGet("~/api/customers/{customerId:long}/payments")]
    [ProducesResponseType<IReadOnlyList<PaymentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> ListByCustomer(
        long customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await _service.ListByCustomerAsync(customerId, page, pageSize, ct));

    /// <summary>Створює платіж разом з деталями та розподілом на рахунки в одній транзакції.</summary>
    [HttpPost]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Create(CreatePaymentDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:long}/allocations")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> AddAllocation(long id, AllocationRequestDto dto, CancellationToken ct) =>
        Ok(await _service.AddAllocationAsync(id, dto, ct));

    /// <summary>Зміна статусу: Created → Validating → Authorized → Captured → Settled; скасування — окремий перехід.</summary>
    [HttpPost("{id:long}/status")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> ChangeStatus(long id, ChangePaymentStatusDto dto, CancellationToken ct) =>
        Ok(await _service.ChangeStatusAsync(id, dto, ct));

    [HttpPost("{id:long}/authorize")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Authorize(long id, AuthorizePaymentDto dto, CancellationToken ct) =>
        Ok(await _service.AuthorizeAsync(id, dto, ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
