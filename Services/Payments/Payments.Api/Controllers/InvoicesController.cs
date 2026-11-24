using Microsoft.AspNetCore.Mvc;
using Payments.Bll.Dto;
using Payments.Bll.Services;

namespace Payments.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Produces("application/json")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _service;

    public InvoicesController(IInvoiceService service) => _service = service;

    [HttpGet("{id:long}")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> Get(long id, CancellationToken ct) =>
        Ok(await _service.GetAsync(id, ct));

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> List(CancellationToken ct) =>
        Ok(await _service.ListAsync(ct));

    [HttpPost]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Update(long id, UpdateInvoiceDto dto, CancellationToken ct) =>
        Ok(await _service.UpdateAsync(id, dto, ct));
}
