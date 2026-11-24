using Disputes.Domain.Common;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using MongoDB.Bson;

namespace Disputes.Domain.Entities;

/// <summary>Тема обговорення спору (колекція disputeThreads). Повідомлення живуть окремо — reference threadId.</summary>
public class DisputeThread : BaseEntity
{
    public ObjectId DisputeId { get; private set; }
    public string Title { get; private set; } = "";
    public ThreadStatus Status { get; private set; }
    public int MessagesCount { get; private set; }
    public DateTime? LastMessageAt { get; private set; }

    private DisputeThread() { }   // для десеріалізації

    public DisputeThread(ObjectId disputeId, string title)
    {
        if (disputeId == ObjectId.Empty) throw new DomainException("DisputeId is required");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Thread title is required");
        if (title.Length > 200) throw new DomainException("Thread title is too long (max 200)");

        DisputeId = disputeId;
        Title = title.Trim();
        Status = ThreadStatus.Open;
    }

    public void Close()
    {
        Status = ThreadStatus.Closed;
        Touch();
    }
}
