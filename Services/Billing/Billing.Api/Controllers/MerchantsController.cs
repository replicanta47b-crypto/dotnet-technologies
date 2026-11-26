using Platform.Security;
using Billing.Bll.Dto;
using Billing.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

[ApiController]
[Route("api/merchants")]
[Produces("application/json")]
public class MerchantsController : ControllerBase
{
    private readonly IMerchantService _service;

    public MerchantsController(IMerchantService service) => _service = service;

    [RequirePermission(PlatformPermissions.Billing.Read)]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MerchantDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MerchantDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    /// <summary>Мерчант з рахунками (Explicit Loading).</summary>
    [RequirePermission(PlatformPermissions.Billing.Read)]
    [HttpGet("{id:long}")]
    [ProducesResponseType<MerchantDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MerchantDetailsDto>> GetById(long id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [RequirePermission(PlatformPermissions.Billing.Create)]
    [HttpPost]
    [ProducesResponseType<MerchantDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MerchantDto>> Create(
        CreateMerchantDto input, [FromServices] IValidator<CreateMerchantDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var created = await _service.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Перейменування мерчанта: публікує MerchantRenamedEvent — Payments і Disputes оновлять свої копії назви.</summary>
    [RequirePermission(PlatformPermissions.Billing.Update)]
    [HttpPut("{id:long}/name")]
    [ProducesResponseType<MerchantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MerchantDto>> Rename(
        long id, RenameMerchantDto input, [FromServices] IValidator<RenameMerchantDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return Ok(await _service.RenameAsync(id, input, ct));
    }
}
