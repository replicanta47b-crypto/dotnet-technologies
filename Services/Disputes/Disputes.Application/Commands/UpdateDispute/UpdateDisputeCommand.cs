using Disputes.Application.Common.Interfaces;

namespace Disputes.Application.Commands.UpdateDispute;

/// <summary>Зміна статусу спору. ExpectedVersion (з If-Match) — оптимістична конкурентність.</summary>
public record UpdateDisputeCommand(string Id, string Status, int? ExpectedVersion) : ICommand;
