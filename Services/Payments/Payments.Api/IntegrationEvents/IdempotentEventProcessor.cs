using Payments.Dal.Interfaces;
using Payments.Domain.Exceptions;
using Platform.EventBus;

namespace Payments.Api.IntegrationEvents;

/// <summary>
/// Ідемпотентне виконання обробника події. В ОДНІЙ транзакції: (1) вставка EventId у dbo.ProcessedEvents,
/// (2) бізнес-зміна, (3) Commit. Якщо EventId уже є (повторна доставка) — зміна пропускається, а факт дубліката логується.
/// Збій на будь-якому кроці відкочує все, зокрема й запис EventId, тож повтор події застосує її знову.
/// </summary>
public sealed class IdempotentEventProcessor(IUnitOfWork uow, ILogger<IdempotentEventProcessor> logger)
{
    public async Task ProcessAsync(IntegrationEvent @event, Func<IUnitOfWork, Task> apply, CancellationToken ct)
    {
        var eventType = @event.GetType().Name;
        await uow.BeginTransactionAsync(ct: ct);
        try
        {
            if (!await uow.ProcessedEvents.TryAddAsync(@event.EventId, eventType, ct))
            {
                await uow.RollbackAsync(ct);
                logger.LogInformation("Duplicate {EventType} {EventId} skipped: already processed", eventType, @event.EventId);
                return;
            }

            await apply(uow);
            await uow.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is BusinessConflictException or Payments.Domain.Exceptions.ValidationException)
        {
            await uow.RollbackAsync(CancellationToken.None);
            // порушення бізнес-правила / обмеження БД від повтору не зникне → одразу в DLQ
            throw new PermanentEventException($"{eventType} {@event.EventId} violates a business rule: {ex.Message}", ex);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;   // тимчасовий збій (БД недоступна, таймаут, deadlock) → повтор із лічильником
        }
    }
}
