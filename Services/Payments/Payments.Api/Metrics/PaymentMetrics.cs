using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Payments.Api.Metrics;

/// <summary>
/// Бізнес-метрики сервісу платежів (Meter "Payments.Api", експорт у Aspire Dashboard через OTLP).
/// Singleton: Meter живе весь час роботи застосунку. Meter додано в ServiceDefaults (AddMeter).
///
/// Теги (однакові назви в усіх сервісах; усі значення з обмеженого набору, тож низька кардинальність):
///   payment.status — Pending | Authorized | Captured | ... (статус платежу)
///   currency       — код валюти (UAH, USD, EUR)
///   operation      — "{Controller}.{Action}", ~20 значень
///   status         — success | failure (результат операції)
/// Ідентифікатори платежів, клієнтів і Correlation-Id в тегах НЕ використовуються: кожне унікальне значення — окремий часовий ряд.
/// </summary>
public sealed class PaymentMetrics
{
    public const string MeterName = "Payments.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _statusChanged;
    private readonly Counter<long> _allocationsAdded;
    private readonly Histogram<double> _amount;
    private readonly Histogram<double> _operationDuration;
    private int _inFlight;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>("payments.created", unit: "{payment}", description: "Кількість створених платежів");
        _statusChanged = meter.CreateCounter<long>("payments.status_changed", unit: "{payment}", description: "Зміни статусу платежу (авторизація, списання, скасування)");
        _allocationsAdded = meter.CreateCounter<long>("payments.allocations_added", unit: "{allocation}", description: "Розподіли платежу на рахунки");
        _amount = meter.CreateHistogram<double>("payments.amount", unit: "{amount}", description: "Розподіл сум створених платежів (за валютою)");
        _operationDuration = meter.CreateHistogram<double>("payments.operation.duration", unit: "ms", description: "Тривалість операцій API (успішних і невдалих)");

        // gauge: знімок поточного стану в момент збирання метрик (лічильник лише зростає, gauge — ні)
        meter.CreateObservableGauge("payments.operations.in_flight", () => Volatile.Read(ref _inFlight),
            unit: "{operation}", description: "Операції API, що виконуються просто зараз");
    }

    public void OperationStarted() => Interlocked.Increment(ref _inFlight);
    public void OperationFinished() => Interlocked.Decrement(ref _inFlight);

    public void PaymentCreated(string status, string currency, decimal amount)
    {
        _created.Add(1, new TagList { { "payment.status", status }, { "currency", currency } });
        _amount.Record((double)amount, new TagList { { "currency", currency } });
    }

    public void StatusChanged(string status) =>
        _statusChanged.Add(1, new TagList { { "payment.status", status } });

    public void AllocationAdded() => _allocationsAdded.Add(1);

    /// <summary>Записується для ВСІХ виконань, включно з невдалими, інакше картина продуктивності викривлена.</summary>
    public void RecordDuration(string operation, bool success, double ms) =>
        _operationDuration.Record(ms, new TagList { { "operation", operation }, { "status", success ? "success" : "failure" } });
}
