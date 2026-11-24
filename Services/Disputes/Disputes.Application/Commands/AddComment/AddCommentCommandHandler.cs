using AutoMapper;
using Disputes.Application.Dtos;
using Disputes.Domain.Exceptions;
using Disputes.Domain.Interfaces;
using MediatR;
using MongoDB.Bson;

namespace Disputes.Application.Commands.AddComment;

public class AddCommentCommandHandler(IDisputeRepository disputes, IMapper mapper)
    : IRequestHandler<AddCommentCommand, DisputeDetailsDto>
{
    public async Task<DisputeDetailsDto> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var dispute = await disputes.GetByIdAsync(ObjectId.Parse(cmd.DisputeId), ct)
                      ?? throw new NotFoundException("Dispute", cmd.DisputeId);

        if (cmd.ExpectedVersion is { } expected && expected != dispute.Version)
            throw new ConflictException(
                $"Dispute {cmd.DisputeId} has version {dispute.Version}, but version {expected} was expected");

        dispute.AddComment(cmd.Author, cmd.Text);
        await disputes.UpdateAsync(dispute, ct);

        return mapper.Map<DisputeDetailsDto>(dispute);
    }
}
