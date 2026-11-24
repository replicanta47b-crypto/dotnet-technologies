using MediatR;

namespace Disputes.Application.Common.Interfaces;

/// <summary>Команда без результату (запис/зміна/видалення).</summary>
public interface ICommand : IRequest { }

/// <summary>Команда з результатом.</summary>
public interface ICommand<TResponse> : IRequest<TResponse> { }

/// <summary>Запит на читання. Не змінює стан системи.</summary>
public interface IQuery<TResponse> : IRequest<TResponse> { }
