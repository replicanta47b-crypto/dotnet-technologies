using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Disputes.WebApi.Metrics;

/// <summary>
/// Бізнес-метрики сервісу спорів (Meter "Disputes.Api"). Singleton; Meter додано в ServiceDefaults (AddMeter).
/// Теги: operation = "{Controller}.{Action}", status = success | failure. Ідентифікатори спорів у тегах не використовуються.
/// </summary>
public sealed class DisputeMetrics
{
    public const string MeterName = "Disputes.Api";

    private readonly Counter<long> _opened;
    private readonly Counter<long> _commentsAdded;
    private readonly Histogram<double> _operationDuration;
    private int _inFlight;

    public DisputeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _opened = meter.CreateCounter<long>("disputes.opened", unit: "{dispute}", description: "Кількість відкритих спорів");
        _commentsAdded = meter.CreateCounter<long>("disputes.comments_added", unit: "{comment}", description: "Коментарі до спорів");
        _operationDuration = meter.CreateHistogram<double>("disputes.operation.duration", unit: "ms", description: "Тривалість операцій API (успішних і невдалих)");
        meter.CreateObservableGauge("disputes.operations.in_flight", () => Volatile.Read(ref _inFlight),
            unit: "{operation}", description: "Операції API, що виконуються просто зараз");
    }

    public void OperationStarted() => Interlocked.Increment(ref _inFlight);
    public void OperationFinished() => Interlocked.Decrement(ref _inFlight);
    public void DisputeOpened() => _opened.Add(1);
    public void CommentAdded() => _commentsAdded.Add(1);

    public void RecordDuration(string operation, bool success, double ms) =>
        _operationDuration.Record(ms, new TagList { { "operation", operation }, { "status", success ? "success" : "failure" } });
}
