using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Billing.Api.Metrics;

/// <summary>
/// Бізнес-метрики сервісу білінгу (Meter "Billing.Api"). Singleton; Meter додано в ServiceDefaults (AddMeter).
/// Метрики кешу (cache.hits / cache.misses, Meter "Billing.Cache") — окремо, у Billing.Bll/Caching/CacheMetrics.cs.
///
/// Теги (набори значень обмежені → низька кардинальність):
///   invoice.status — Issued | Paid | Void | Overdue
///   currency       — код валюти
///   operation      — "{Controller}.{Action}"
///   status         — success | failure
/// Ідентифікатори рахунків і мерчантів у тегах не використовуються.
/// </summary>
public sealed class BillingMetrics
{
    public const string MeterName = "Billing.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _statusChanged;
    private readonly Histogram<double> _amount;
    private readonly Histogram<double> _operationDuration;
    private int _inFlight;

    public BillingMetrics(IMeterFactory meterFactory, IMemoryCache l1)
    {
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>("invoices.created", unit: "{invoice}", description: "Кількість створених рахунків");
        _statusChanged = meter.CreateCounter<long>("invoices.status_changed", unit: "{invoice}", description: "Зміни статусу рахунку (оплата, скасування)");
        _amount = meter.CreateHistogram<double>("invoices.amount", unit: "{amount}", description: "Розподіл сум створених рахунків (за валютою)");
        _operationDuration = meter.CreateHistogram<double>("invoices.operation.duration", unit: "ms", description: "Тривалість операцій API (успішних і невдалих)");

        // gauge: поточний стан, читається callback-ом у момент збирання метрик
        meter.CreateObservableGauge("invoices.operations.in_flight", () => Volatile.Read(ref _inFlight),
            unit: "{operation}", description: "Операції API, що виконуються просто зараз");
        meter.CreateObservableGauge("billing.cache.l1.entries", () => l1.GetCurrentStatistics()?.CurrentEntryCount ?? 0L,
            unit: "{entry}", description: "Кількість записів у L1 (IMemoryCache)");
        meter.CreateObservableGauge("billing.cache.l1.size", () => l1.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0L,
            unit: "{size}", description: "Зайнятий «бюджет» L1 (сума Size записів; SizeLimit — у конфігурації Cache)");
    }

    public void OperationStarted() => Interlocked.Increment(ref _inFlight);
    public void OperationFinished() => Interlocked.Decrement(ref _inFlight);

    public void InvoiceCreated(string status, string currency, decimal amount)
    {
        _created.Add(1, new TagList { { "invoice.status", status }, { "currency", currency } });
        _amount.Record((double)amount, new TagList { { "currency", currency } });
    }

    public void StatusChanged(string status) =>
        _statusChanged.Add(1, new TagList { { "invoice.status", status } });

    /// <summary>Записується для ВСІХ виконань, включно з невдалими.</summary>
    public void RecordDuration(string operation, bool success, double ms) =>
        _operationDuration.Record(ms, new TagList { { "operation", operation }, { "status", success ? "success" : "failure" } });
}
