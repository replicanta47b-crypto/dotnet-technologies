using System.Collections.Concurrent;

namespace Settlements.Api;

public sealed record Settlement(Guid Id, long MerchantId, decimal Amount, string Currency, string Status, DateTime CreatedAtUtc, string? Reason = null);

public sealed record ProcessSettlementRequest(long MerchantId, decimal Amount, string Currency = "UAH");

public sealed record RefundRequest(Guid SettlementId, decimal Amount, string? Reason);

/// <summary>Сховище в пам'яті: сервіс-заглушка не має власної БД.</summary>
public sealed class SettlementStore
{
    private readonly ConcurrentDictionary<Guid, Settlement> _items = new();

    public SettlementStore()
    {
        foreach (var (merchant, amount) in new[] { (1L, 12500.50m), (2L, 4200m), (3L, 3100.75m) })
        {
            var s = new Settlement(Guid.NewGuid(), merchant, amount, "UAH", "Processed", DateTime.UtcNow.AddDays(-merchant));
            _items[s.Id] = s;
        }
    }

    public IReadOnlyList<Settlement> All() => _items.Values.OrderByDescending(x => x.CreatedAtUtc).ToList();

    public Settlement Add(Settlement settlement) => _items[settlement.Id] = settlement;

    public Settlement? Find(Guid id) => _items.GetValueOrDefault(id);
}
