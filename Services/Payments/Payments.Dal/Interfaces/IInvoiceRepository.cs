using Payments.Domain.Models;

namespace Payments.Dal.Interfaces;

/// <summary>Локальна копія довідника рахунків (власник даних — Білінг).</summary>
public interface IInvoiceRepository : IGenericRepository<Invoice>
{
    Task<Invoice?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task<bool> UpdateAsync(Invoice invoice, CancellationToken ct = default);

    /// <summary>
    /// Лаб. 08: створює або оновлює локальну копію рахунку за подією з Білінгу. Зміна застосовується лише якщо вона НОВІША
    /// за останню застосовану (порівняння з UpdatedAt) — запізніла подія не перезапише новіші дані (out-of-order).
    /// Повертає: Inserted / Updated / Stale (застаріла подія пропущена).
    /// </summary>
    Task<SnapshotResult> UpsertSnapshotAsync(Invoice snapshot, DateTime occurredAtUtc, CancellationToken ct = default);

    /// <summary>Лаб. 08: оновлює MerchantName у всіх копіях рахунків мерчанта (за старою назвою). Повертає кількість рядків.</summary>
    Task<int> RenameMerchantAsync(string oldName, string newName, DateTime occurredAtUtc, CancellationToken ct = default);
}

public enum SnapshotResult { Inserted, Updated, Stale }
}
