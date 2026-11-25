using Billing.Bll.Dto;
using Billing.Bll.Services;
using Billing.Bll.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

/// <summary>Тонкий контролер: валідація → виклик сервісу → відповідь. До DbContext не звертається.</summary>
[ApiController]
[Route("api/invoices")]
[Produces("application/json")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _service;

    public InvoicesController(IInvoiceService service) => _service = service;

    /// <summary>Список рахунків: пагінація, фільтри (search, status, merchantId, minAmount, maxAmount), сортування (sortBy=number|amount|dueDate, sortDir).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<InvoiceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<InvoiceDto>>> GetList([FromQuery] InvoiceQuery query, CancellationToken ct)
    {
        var result = await _service.GetListAsync(query, ct);
        // метадані пагінації також у заголовках (зручно для кешування й клієнтів)
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();
        Response.Headers["X-Total-Pages"] = result.TotalPages.ToString();
        return Ok(result);
    }

    /// <summary>Прострочені рахунки (інша специфікація — той самий репозиторій).</summary>
    [HttpGet("overdue")]
    [ProducesResponseType<IReadOnlyList<InvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetOverdue(CancellationToken ct)
        => Ok(await _service.GetOverdueAsync(ct));

    /// <summary>Рахунки категорії, призначені після дати since (за замовчуванням — останні 30 днів). M:N через проміжну сутність.</summary>
    [HttpGet("~/api/categories/{slug}/invoices")]
    [ProducesResponseType<IReadOnlyList<InvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetByCategory(
        string slug, [FromQuery] DateTime? since, CancellationToken ct)
        => Ok(await _service.GetByCategoryAsync(slug, since, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDetailsDto>> GetById(long id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDetailsDto>> Create(
        CreateInvoiceDto input, [FromServices] IValidator<CreateInvoiceDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var created = await _service.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);   // 201 + Location
    }

    [HttpPost("{id:long}/pay")]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDetailsDto>> MarkPaid(long id, CancellationToken ct)
        => Ok(await _service.MarkPaidAsync(id, ct));

    [HttpPost("{id:long}/void")]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDetailsDto>> Void(long id, CancellationToken ct)
        => Ok(await _service.VoidAsync(id, ct));

    [HttpPut("{id:long}/due-date")]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDetailsDto>> Reschedule(
        long id, RescheduleInvoiceDto input, [FromServices] IValidator<RescheduleInvoiceDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _service.RescheduleAsync(id, input, ct));
    }

    /// <summary>Донарахування до виставленого рахунку: змінює суму й публікує InvoiceAmountChangedEvent.</summary>
    [HttpPost("{id:long}/surcharges")]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDetailsDto>> AddSurcharge(
        long id, AddSurchargeDto input, [FromServices] IValidator<AddSurchargeDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _service.AddSurchargeAsync(id, input, ct));
    }

    [HttpPost("{id:long}/categories")]
    [ProducesResponseType<InvoiceDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDetailsDto>> AssignCategory(
        long id, AssignCategoryDto input, [FromServices] IValidator<AssignCategoryDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _service.AssignCategoryAsync(id, input, ct));
    }

    /// <summary>М'яке видалення (лише Draft/Void).</summary>
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
