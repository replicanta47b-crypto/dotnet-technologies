using Disputes.Application.Common.Interfaces;

namespace Disputes.Application.Commands.DeleteDispute;

public record DeleteDisputeCommand(string Id) : ICommand;
