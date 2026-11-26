using Platform.Security;
using AutoMapper;
using Disputes.Application.Commands.AddComment;
using Disputes.Application.Commands.CreateDispute;
using Disputes.Application.Commands.DeleteDispute;
using Disputes.Application.Commands.UpdateDispute;
using Disputes.Application.Common.Models;
using Disputes.Application.Dtos;
using Disputes.Application.Queries.GetDisputeById;
using Disputes.Application.Queries.GetDisputesList;
using Disputes.WebApi.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Disputes.WebApi.Controllers;

[Route("api/disputes")]
[Produces("application/json")]
public class DisputesController(ISender mediator, IMapper mapper) : ApiControllerBase(mediator)
{
    /// <summary>Список спорів: фільтри, повнотекстовий пошук, сортування й пагінація виконуються в MongoDB.</summary>
    [RequirePermission(PlatformPermissions.Disputes.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DisputeDto>>> GetList(
        [FromQuery] long? merchantId,
        [FromQuery] long? paymentId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool desc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await Mediator.Send(
            new GetDisputesListQuery(merchantId, paymentId, status, search, sortBy, desc, page, pageSize), ct));

    /// <summary>Спір за Id. У заголовку ETag — версія документа (для If-Match у PUT).</summary>
    [RequirePermission(PlatformPermissions.Disputes.Read)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(DisputeDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DisputeDetailsDto>> GetById(string id, CancellationToken ct)
    {
        var dispute = await Mediator.Send(new GetDisputeByIdQuery(id), ct);
        Response.Headers.ETag = $"\"{dispute.Version}\"";
        return Ok(dispute);
    }

    /// <summary>Відкриття спору. 201 + Location із рядковим ObjectId.</summary>
    [RequirePermission(PlatformPermissions.Disputes.Create)]
    [HttpPost]
    [ProducesResponseType(typeof(string), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<string>> Create(CreateDisputeRequest request, CancellationToken ct)
    {
        var id = await Mediator.Send(mapper.Map<CreateDisputeCommand>(request), ct);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    /// <summary>Зміна статусу спору. Заголовок If-Match: "версія" вмикає оптимістичну конкурентність.</summary>
    [RequirePermission(PlatformPermissions.Disputes.Update)]
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, UpdateDisputeRequest request, CancellationToken ct)
    {
        var command = mapper.Map<UpdateDisputeCommand>(request) with { Id = id, ExpectedVersion = ReadIfMatch() };
        await Mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>Додає коментар до спору й повертає оновлений спір.</summary>
    [RequirePermission(PlatformPermissions.Disputes.Create)]
    [HttpPost("{id}/comments")]
    [ProducesResponseType(typeof(DisputeDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeDetailsDto>> AddComment(
        string id, AddCommentRequest request, CancellationToken ct)
    {
        var command = mapper.Map<AddCommentCommand>(request) with { DisputeId = id, ExpectedVersion = ReadIfMatch() };
        return Ok(await Mediator.Send(command, ct));
    }

    /// <summary>Видалення спору (разом із темою обговорення).</summary>
    [RequirePermission(PlatformPermissions.Disputes.Delete)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await Mediator.Send(new DeleteDisputeCommand(id), ct);
        return NoContent();
    }

    /// <summary>Читає очікувану версію з If-Match ("3" або 3). Немає заголовка — перевірка не виконується.</summary>
    private int? ReadIfMatch()
    {
        var header = Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return int.TryParse(header, out var version) ? version : null;
    }
}
