using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Disputes.WebApi.Controllers;

/// <summary>Базовий контролер: усі дії лише передають команди й запити в MediatR (ISender).</summary>
[ApiController]
public abstract class ApiControllerBase(ISender mediator) : ControllerBase
{
    protected ISender Mediator { get; } = mediator;
}
