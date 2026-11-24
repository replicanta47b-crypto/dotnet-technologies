using Disputes.Application.Common.Interfaces;
using Disputes.Application.Dtos;

namespace Disputes.Application.Commands.AddComment;

/// <summary>Додає коментар (елемент вкладеного масиву comments) і повертає оновлений спір.</summary>
public record AddCommentCommand(string DisputeId, string Author, string Text, int? ExpectedVersion)
    : ICommand<DisputeDetailsDto>;
